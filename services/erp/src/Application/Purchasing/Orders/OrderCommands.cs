using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record CreatePoCommand(PoInput Input) : IRequest<PoDto>;

public sealed class CreatePoCommandValidator : AbstractValidator<CreatePoCommand> {
    public CreatePoCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new PoInputValidator());
    }
}

public sealed class CreatePoCommandHandler(
    IApplicationDbContext db,
    PoBuilder             builder,
    PoMapper              mapper
) : IRequestHandler<CreatePoCommand, PoDto> {
    public async Task<PoDto> Handle(CreatePoCommand request, CancellationToken ct) {
        var po = new PurchaseOrder { Status = PurchaseOrderStatus.Draft };
        await builder.ApplyAsync(po, request.Input, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        po.Number = await DocumentNumbers.NextAsync(db, "SA", po.Date, ct);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(po, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record UpdatePoCommand(Guid Id, PoInput Input) : IRequest<PoDto>;

public sealed class UpdatePoCommandValidator : AbstractValidator<UpdatePoCommand> {
    public UpdatePoCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new PoInputValidator());
    }
}

public sealed class UpdatePoCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    PoBuilder             builder,
    PoMapper              mapper,
    TimeProvider          clock
) : IRequestHandler<UpdatePoCommand, PoDto> {
    public async Task<PoDto> Handle(UpdatePoCommand request, CancellationToken ct) {
        var po = await PoRules.LoadAsync(db, tenant, request.Id, true, ct);
        PoRules.EnsureDraft(po);
        if (request.Input.Version is { } version && version != po.Version) throw PurchasingErrors.VersionConflict();

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            // Önce eski satırlar silinir (satır sırası tekil indeksi), sonra yenileri ve başlık yazılır.
            db.PurchaseOrderLines.RemoveRange(po.Lines);
            po.Lines.Clear();
            await db.SaveChangesAsync(ct);

            await builder.ApplyAsync(po, request.Input, ct);
            po.LastModified = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw PurchasingErrors.VersionConflict();
        }

        return await mapper.ToDtoAsync(po, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record ApprovePoCommand(Guid Id) : IRequest<PoDto>;

public sealed class ApprovePoCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    PoMapper              mapper
) : IRequestHandler<ApprovePoCommand, PoDto> {
    public async Task<PoDto> Handle(ApprovePoCommand request, CancellationToken ct) {
        var po = await PoRules.LoadAsync(db, tenant, request.Id, true, ct);
        PoRules.EnsureDraft(po);
        po.Status = PurchaseOrderStatus.Approved;
        await db.SaveChangesAsync(ct);
        return await mapper.ToDtoAsync(po, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record CancelPoCommand(Guid Id, string Reason) : IRequest<PoDto>;

public sealed class CancelPoCommandValidator : AbstractValidator<CancelPoCommand> {
    public CancelPoCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.")
            .Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelPoCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    IPurchaseLinkListener links,
    PoMapper              mapper
) : IRequestHandler<CancelPoCommand, PoDto> {
    public async Task<PoDto> Handle(CancelPoCommand request, CancellationToken ct) {
        // Kapatmayla aynı sıra: sipariş kilidi, kilit altında okuma, rezervasyon depolarının stok kilitleri, bırakma. Sipariş kilidi iptal ile mal kabul yarışını da sıraya sokar.
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockPurchaseOrderAsync(request.Id, ct)) throw new NotFoundException(request.Id.ToString(), "PurchaseOrder");
        var po = await PoRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (po.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed)
            throw PurchasingErrors.Conflict("po_closed", "Sipariş zaten kapatılmış ya da iptal edilmiş.");

        var hasReceipts = po.Status is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received
                          || await db.GoodsReceipts.AnyAsync(r => r.PurchaseOrderId == po.Id && r.Status == GoodsReceiptStatus.Posted, ct);
        if (hasReceipts) throw PurchasingErrors.PoHasReceipts();

        po.Status       = PurchaseOrderStatus.Cancelled;
        po.CancelReason = request.Reason.Trim();

        var keys = await db.Reservations.AsNoTracking().Where(r => r.ExpectedReceiptSourceId == po.Id && r.Status == ReservationStatus.Active)
                           .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct);
        await db.LockStockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)), ct);
        var released = await stock.ReleaseExpectedAsync(po.Id, ct);
        await links.OnPoClosedAsync(po.Id, ct);

        outbox.Enqueue("purchase.order-cancelled", new {
            poId = po.Id, number = po.Number, reason = po.CancelReason,
            lines = po.Lines.Select(l => new { poLineId = l.Id, salesOrderLineId = l.SalesOrderLineId, productId = l.ProductId, variantId = l.VariantId, quantity = l.Quantity }).ToList(),
            releasedReservations = released.Select(r => new { sourceType = r.SourceType, sourceId = r.SourceId, sourceLineId = r.SourceLineId, productId = r.ProductId, variantId = r.VariantId, quantity = r.Quantity }).ToList()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(po, ct);
    }
}

/// <summary>Kısa kapatma: kalan açık miktar serbest bırakılır; sonraki mal kabul reddedilir.</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record ClosePoCommand(Guid Id, string Reason) : IRequest<PoDto>;

public sealed class ClosePoCommandValidator : AbstractValidator<ClosePoCommand> {
    public ClosePoCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Kapatma nedeni zorunludur.")
            .Length(2, 200).WithMessage("Kapatma nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class ClosePoCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    IPurchaseLinkListener links,
    PoMapper              mapper
) : IRequestHandler<ClosePoCommand, PoDto> {
    public async Task<PoDto> Handle(ClosePoCommand request, CancellationToken ct) {
        // Sıra: sipariş kilidi, siparişin kilit altında okunması, rezervasyon depolarının stok kilitleri, bırakma.
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockPurchaseOrderAsync(request.Id, ct)) throw new NotFoundException(request.Id.ToString(), "PurchaseOrder");
        var po = await PoRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (po.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw PurchasingErrors.Conflict("po_not_open", "Yalnızca onaylı ya da kısmen gelmiş siparişler kapatılabilir.");

        var closed = po.Lines.Where(l => l.OpenQuantity > 0m).Select(l => new { poLineId = l.Id, salesOrderLineId = l.SalesOrderLineId, productId = l.ProductId, variantId = l.VariantId, quantity = l.OpenQuantity }).ToList();
        foreach (var line in po.Lines) line.ClosedQuantity += line.OpenQuantity;
        po.Status      = PurchaseOrderStatus.Closed;
        po.CloseReason = request.Reason.Trim();

        var keys = await db.Reservations.AsNoTracking().Where(r => r.ExpectedReceiptSourceId == po.Id && r.Status == ReservationStatus.Active)
                           .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct);
        await db.LockStockAsync(keys.Select(k => (k.WarehouseId, k.ProductId, k.VariantId)), ct);
        var released = await stock.ReleaseExpectedAsync(po.Id, ct);
        await links.OnPoClosedAsync(po.Id, ct);

        outbox.Enqueue("purchase.order-closed", new {
            poId = po.Id, number = po.Number, reason = po.CloseReason, lines = closed,
            releasedReservations = released.Select(r => new { sourceType = r.SourceType, sourceId = r.SourceId, sourceLineId = r.SourceLineId, productId = r.ProductId, variantId = r.VariantId, quantity = r.Quantity }).ToList()
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(po, ct);
    }
}

/// <summary>Aynı satırlarla yeni bir taslak üretir (tarih bugün, gelen miktarlar sıfır).</summary>
[Authorize(Permission = PurchasingPermissions.Manage)]
public sealed record CopyPoCommand(Guid Id) : IRequest<PoDto>;

public sealed class CopyPoCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    PoMapper              mapper,
    TimeProvider          time
) : IRequestHandler<CopyPoCommand, PoDto> {
    public async Task<PoDto> Handle(CopyPoCommand request, CancellationToken ct) {
        if (!session.HasPermission(PurchasingPermissions.PurchasePrice)) throw new ForbiddenAccessException();
        var source = await PoRules.LoadAsync(db, tenant, request.Id, false, ct);
        var today  = CurrentAccountClock.Today(time);

        var copy = new PurchaseOrder {
            Status = PurchaseOrderStatus.Draft, SupplierId = source.SupplierId, BranchId = source.BranchId, Date = today,
            ExpectedDate = null, Currency = source.Currency, Rate = source.Rate, Notes = source.Notes, Gross = source.Gross,
            Discount = source.Discount, Net = source.Net, Vat = source.Vat, Total = source.Total
        };
        foreach (var l in source.Lines.OrderBy(l => l.Sort))
            copy.Lines.Add(new PurchaseOrderLine {
                PurchaseOrderId = copy.Id, Sort = l.Sort, ProductId = l.ProductId, VariantId = l.VariantId, Description = l.Description, Unit = l.Unit,
                Factor = l.Factor, Quantity = l.Quantity, Width = l.Width, Length = l.Length, UnitPrice = l.UnitPrice, DiscountPercent = l.DiscountPercent,
                VatRate = l.VatRate, ExpectedDate = today, Net = l.Net, Vat = l.Vat, Total = l.Total
            });

        await using var tx = await db.BeginTransactionAsync(ct);
        copy.Number = await DocumentNumbers.NextAsync(db, "SA", today, ct);
        db.PurchaseOrders.Add(copy);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(copy, ct);
    }
}
