using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Documents;
using ERP.Application.Purchasing;
using ERP.Application.Purchasing.Orders;
using ERP.Domain.Purchasing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record CreateOrderCommand(OrderInput Input) : IRequest<OrderDto>;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> {
    public CreateOrderCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new OrderInputValidator());
    }
}

public sealed class CreateOrderCommandHandler(
    IApplicationDbContext db,
    OrderWriter           writer,
    OrderMapper           mapper
) : IRequestHandler<CreateOrderCommand, OrderDto> {
    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken ct) {
        var order = new SalesOrder { Status = SalesOrderStatus.Draft };
        await writer.ApplyAsync(order, request.Input, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        order.Number = await DocumentNumbers.NextAsync(db, "SS", order.Date, ct);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(order, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record UpdateOrderCommand(Guid Id, OrderInput Input) : IRequest<OrderDto>;

public sealed class UpdateOrderCommandValidator : AbstractValidator<UpdateOrderCommand> {
    public UpdateOrderCommandValidator() {
        RuleFor(c => c.Input).NotNull().SetValidator(new OrderInputValidator());
    }
}

public sealed class UpdateOrderCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    OrderWriter           writer,
    OrderMapper           mapper,
    SalesCurrency         currency,
    TimeProvider          clock
) : IRequestHandler<UpdateOrderCommand, OrderDto> {
    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken ct) {
        var order = await OrderRules.LoadAsync(db, tenant, request.Id, true, ct);
        if (!order.IsEditable) throw SalesErrors.OrderNotDraft();
        if (request.Input.Version is { } version && version != order.Version) throw SalesErrors.VersionConflict();

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            db.SalesOrderLines.RemoveRange(order.Lines);
            order.Lines.Clear();
            await db.SaveChangesAsync(ct);

            await writer.ApplyAsync(order, request.Input, ct);

            // Toplam değiştiyse taksit planı geçersizdir; yeniden kurulmalıdır.
            if (order.Plan is { } plan) {
                var cari  = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == order.CurrentAccountId).Select(c => c.Currency).FirstAsync(ct);
                var total = await currency.ConvertAsync(order.Total, order.Currency, cari, order.Date, ct);
                if (plan.DownPayment + plan.Rows.Sum(r => r.Amount) != total) {
                    db.InstallmentPlanRows.RemoveRange(plan.Rows);
                    db.InstallmentPlans.Remove(plan);
                    order.Plan = null;
                }
            }

            order.LastModified = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }

        return await mapper.ToDtoAsync(order, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Approve)]
public sealed record ApproveOrderCommand(Guid Id) : IRequest<OrderDto>;

public sealed class ApproveOrderCommandHandler(
    IApplicationDbContext db,
    ApprovalPipeline      pipeline,
    OrderMapper           mapper
) : IRequestHandler<ApproveOrderCommand, OrderDto> {
    public async Task<OrderDto> Handle(ApproveOrderCommand request, CancellationToken ct) {
        return await ApprovalRunner.RunAsync(db, pipeline, mapper, request.Id, ApprovalEntry.Approve, ct);
    }
}

[Authorize(Permission = SalesPermissions.LimitOverride)]
public sealed record ApproveLimitCommand(Guid Id) : IRequest<OrderDto>;

public sealed class ApproveLimitCommandHandler(
    IApplicationDbContext db,
    ApprovalPipeline      pipeline,
    OrderMapper           mapper
) : IRequestHandler<ApproveLimitCommand, OrderDto> {
    public async Task<OrderDto> Handle(ApproveLimitCommand request, CancellationToken ct) {
        return await ApprovalRunner.RunAsync(db, pipeline, mapper, request.Id, ApprovalEntry.LimitOverride, ct);
    }
}

internal static class ApprovalRunner {
    public static async Task<OrderDto> RunAsync(IApplicationDbContext db, ApprovalPipeline pipeline, OrderMapper mapper, Guid id, ApprovalEntry entry, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        ApprovalResult result;
        try {
            result = await pipeline.RunAsync(id, entry, ct);
            if (result.Shortages.Count > 0) {
                // Hiçbir şey yazılmadı; işlem onaylanmadan atılır.
                await tx.RollbackAsync(ct);
                db.ClearTracking();
                throw new AppException(409, "insufficient_stock", "Yetersiz stok: " + string.Join(", ", result.Shortages.Select(s => s.Product)) + ".") { Details = result.Shortages };
            }

            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }

        return await mapper.ToDtoAsync(result.Order, result.Warnings, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record CancelOrderCommand(Guid Id, string Reason) : IRequest<OrderDto>;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand> {
    public CancelOrderCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.").Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelOrderCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    IPurchaseLinkListener links,
    OrderMapper           mapper
) : IRequestHandler<CancelOrderCommand, OrderDto> {
    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, request.Id, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockCurrentAccountAsync(head.CurrentAccountId, ct);
        if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "SalesOrder");
        db.ClearTracking();
        var order = await OrderRules.LoadAsync(db, tenant, request.Id, true, ct);

        if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed) throw SalesErrors.Conflict("order_not_open", "Sipariş zaten kapatılmış ya da iptal edilmiş.");
        if (order.Lines.Any(l => l.ShippedQuantity > 0m)) throw SalesErrors.Conflict("order_has_shipments", "Sevk edilmiş sipariş iptal edilemez; kalan miktar için siparişi kapatın.");
        if (await db.Shipments.AnyAsync(s => s.SalesOrderId == order.Id && s.Status != ShipmentStatus.Cancelled, ct))
            throw SalesErrors.Conflict("order_has_shipments", "Sevkiyatı olan sipariş iptal edilemez.");

        // GLOBAL KİLİT SIRASI: cari -> satış siparişi -> satın alma siparişi -> stok (bkz. PoCancellation). Satın alma siparişleri (id sırasıyla) stok kilidinden önce
        // kilitlenir ve Draft durumu kilit altında yeniden doğrulanır; P6 iptali PO -> stok sırasıyla çalıştığı için iki yol birbirini kilitleyemez.
        // Yalnızca bu siparişe ait taslak satın alma siparişleri iptal edilir; onaylanmış olanlara dokunulmaz.
        var poIds = order.Lines.Where(l => l.PurchaseOrderId is not null).Select(l => l.PurchaseOrderId!.Value).Distinct().Order().ToList();
        var lineIds = order.Lines.Select(l => l.Id).ToHashSet();
        var lockedPos = new List<Guid>();
        foreach (var poId in poIds) {
            if (!await db.LockPurchaseOrderAsync(poId, ct)) continue;
            lockedPos.Add(poId);
        }

        if (lockedPos.Count > 0)
            foreach (var po in await db.PurchaseOrders.Include(p => p.Lines).Where(p => lockedPos.Contains(p.Id) && p.Status == PurchaseOrderStatus.Draft).OrderBy(p => p.Id).ToListAsync(ct))
                if (po.Lines.All(l => l.SalesOrderLineId is { } s && lineIds.Contains(s)))
                    await PoCancellation.CancelLockedAsync(db, stock, outbox, links, po, "Satış siparişi iptal edildi", ct);

        await stock.ReleaseAsync(SalesStock.SourceType, order.Id, null, ct);

        order.Status       = SalesOrderStatus.Cancelled;
        order.CancelReason = request.Reason.Trim();
        outbox.Enqueue("sales.order-cancelled", new { orderId = order.Id, number = order.Number, reason = order.CancelReason });
        try {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }

        return await mapper.ToDtoAsync(order, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record CloseOrderCommand(Guid Id, string? Reason) : IRequest<OrderDto>;

/// <summary>Kalan açık miktarı bırakarak siparişi kapatır: sevk edilmemiş rezervasyonlar serbest kalır.</summary>
public sealed class CloseOrderCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    IOutbox               outbox,
    OrderMapper           mapper
) : IRequestHandler<CloseOrderCommand, OrderDto> {
    public async Task<OrderDto> Handle(CloseOrderCommand request, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, request.Id, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockCurrentAccountAsync(head.CurrentAccountId, ct);
        if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "SalesOrder");
        db.ClearTracking();
        var order = await OrderRules.LoadAsync(db, tenant, request.Id, true, ct);

        if (order.Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped or SalesOrderStatus.Shipped))
            throw SalesErrors.Conflict("order_not_open", "Yalnızca onaylı ya da sevk edilmiş siparişler kapatılabilir.");

        await stock.ReleaseAsync(SalesStock.SourceType, order.Id, null, ct);
        order.Status      = SalesOrderStatus.Closed;
        order.CancelReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        outbox.Enqueue("sales.order-closed", new { orderId = order.Id, number = order.Number, reason = order.CancelReason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(order, null, ct);
    }
}

[Authorize(Permission = SalesPermissions.Manage)]
public sealed record SetInstallmentPlanCommand(Guid Id, PlanInput Input) : IRequest<OrderDto>;

public sealed class SetInstallmentPlanCommandValidator : AbstractValidator<SetInstallmentPlanCommand> {
    public SetInstallmentPlanCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.DownPayment).GreaterThanOrEqualTo(0m).WithMessage("Peşinat negatif olamaz.").Must(d => decimal.Round(d, 2) == d).WithMessage("Peşinat en fazla 2 ondalık içerebilir.");
        RuleFor(c => c.Input).Must(i => (i.Rows is { Count: > 0 }) != (i.Count is not null)).WithMessage("Taksit satırları ya da taksit sayısı verilmelidir (ikisi birden değil).");
        RuleFor(c => c.Input.Count).InclusiveBetween(1, 36).When(c => c.Input.Count is not null).WithMessage("Taksit sayısı 1 ile 36 arasında olmalıdır.");
        RuleFor(c => c.Input.FirstDueDate).NotNull().When(c => c.Input.Count is not null).WithMessage("İlk vade tarihi zorunludur.");
        RuleFor(c => c.Input.Rows).Must(r => r is null || r.Count <= 36).WithMessage("En çok 36 taksit olabilir.");
        RuleForEach(c => c.Input.Rows).ChildRules(r => {
            r.RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Taksit tutarı sıfırdan büyük olmalıdır.").Must(a => decimal.Round(a, 2) == a).WithMessage("Taksit tutarı en fazla 2 ondalık içerebilir.");
        }).When(c => c.Input.Rows is not null);
    }
}

public sealed class SetInstallmentPlanCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    SalesCurrency         currency,
    OrderMapper           mapper
) : IRequestHandler<SetInstallmentPlanCommand, OrderDto> {
    public async Task<OrderDto> Handle(SetInstallmentPlanCommand request, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, request.Id, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "SalesOrder");
        db.ClearTracking();
        var order = await OrderRules.LoadAsync(db, tenant, request.Id, true, ct);

        if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed) throw SalesErrors.Conflict("order_not_open", "Kapatılmış ya da iptal edilmiş siparişin planı değiştirilemez.");
        if (order.Plan is { } old && (old.Rows.Any(r => r.Remaining != r.Amount) || old.DownPaymentRemaining != old.DownPayment))
            throw SalesErrors.Conflict("plan_consumed", "Faturalarca kullanılmış taksit planı değiştirilemez.");

        var cari  = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == order.CurrentAccountId).Select(c => c.Currency).FirstAsync(ct);
        var total = await currency.ConvertAsync(order.Total, order.Currency, cari, order.Date, ct);
        var input = request.Input;

        IReadOnlyList<PlanRow> rows;
        if (input.Rows is { Count: > 0 } given) {
            rows = given.OrderBy(r => r.DueDate).Select((r, i) => new PlanRow(i + 1, r.DueDate, r.Amount)).ToList();
        } else {
            try {
                rows = InstallmentSplitter.Equal(total, input.DownPayment, input.Count!.Value, input.FirstDueDate!.Value);
            } catch (ArgumentOutOfRangeException) {
                throw SalesErrors.Bad("installment_mismatch", "Peşinat sipariş toplamını aşamaz.");
            }
        }

        if (input.DownPayment + rows.Sum(r => r.Amount) != total)
            throw SalesErrors.Bad("installment_mismatch", $"Peşinat ve taksitler toplamı ({input.DownPayment + rows.Sum(r => r.Amount):N2}) sipariş tutarına ({total:N2}) eşit olmalıdır.");

        if (order.Plan is { } existing) {
            db.InstallmentPlanRows.RemoveRange(existing.Rows);
            db.InstallmentPlans.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        var plan = new InstallmentPlan {
            SalesOrderId = order.Id, DownPayment = input.DownPayment, DownPaymentRemaining = input.DownPayment, InstallmentCount = rows.Count,
            FirstDueDate = rows.Count > 0 ? rows.Min(r => r.DueDate) : order.Date
        };
        foreach (var r in rows) plan.Rows.Add(new InstallmentPlanRow { InstallmentPlanId = plan.Id, Sequence = r.Sequence, DueDate = r.DueDate, Amount = r.Amount, Remaining = r.Amount });
        order.Plan = plan;
        db.InstallmentPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await mapper.ToDtoAsync(order, null, ct);
    }
}
