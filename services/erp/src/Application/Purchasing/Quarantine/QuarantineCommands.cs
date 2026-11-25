using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Purchasing.Receipts;
using ERP.Application.Purchasing.Returns;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Quarantine;

public sealed record QuarantineRowDto(
    Guid     ReceiptLineId,
    Guid     ReceiptId,
    string   ReceiptNumber,
    DateOnly Date,
    string   SupplierName,
    Guid?    PurchaseOrderId,
    Guid     ProductId,
    string   ProductCode,
    string   ProductName,
    Guid?    VariantId,
    string   Unit,
    decimal  ReceivedQuantity,
    decimal  OpenQuantity,
    decimal  OpenBaseQuantity,
    Guid     WarehouseId,
    Guid?    TargetWarehouseId
);

public sealed record QuarantineResultDto(Guid ReceiptLineId, QuarantineState State, decimal OpenQuantity, Guid? ReturnId, Guid? InvoiceId, string? InvoiceNumber);

/// <summary>Karantinadaki açık miktarlar (satır birimi ve temel birim); miktarlar fiyat içermez.</summary>
[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record ListQuarantineQuery(Guid? BranchId) : IRequest<IReadOnlyList<QuarantineRowDto>>;

public sealed class ListQuarantineQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListQuarantineQuery, IReadOnlyList<QuarantineRowDto>> {
    public async Task<IReadOnlyList<QuarantineRowDto>> Handle(ListQuarantineQuery request, CancellationToken ct) {
        var receipts = db.GoodsReceipts.AsNoTracking().Where(r => r.Status == GoodsReceiptStatus.Posted);
        if (request.BranchId is { } branchId) {
            BranchGuard.Ensure(tenant, branchId);
            receipts = receipts.Where(r => r.BranchId == branchId);
        } else if (tenant.BranchIds is { } scoped) {
            var branches = scoped.ToList();
            receipts = receipts.Where(r => branches.Contains(r.BranchId));
        }

        var rows = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.Quarantine == QuarantineState.Held)
                           .Join(receipts, l => l.GoodsReceiptId, r => r.Id, (l, r) => new { Line = l, Receipt = r }).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var lineIds = rows.Select(r => r.Line.Id).ToList();
        var outflow = (await db.StockMovements.AsNoTracking()
                               .Where(m => m.SourceLineId != null && lineIds.Contains(m.SourceLineId.Value) && m.SourceType != ReceiptRules.StockSource && m.Quantity < 0m)
                               .GroupBy(m => new { m.SourceLineId, m.WarehouseId }).Select(g => new { g.Key.SourceLineId, g.Key.WarehouseId, Qty = -g.Sum(m => m.Quantity) })
                               .ToListAsync(ct)).ToDictionary(x => (x.SourceLineId!.Value, x.WarehouseId), x => x.Qty);

        var productIds  = rows.Select(r => r.Line.ProductId).Distinct().ToList();
        var supplierIds = rows.Select(r => r.Receipt.SupplierId).Distinct().ToList();
        var products    = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var suppliers   = await db.CurrentAccounts.AsNoTracking().Where(c => supplierIds.Contains(c.Id)).Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, ct);

        var result = new List<QuarantineRowDto>();
        foreach (var r in rows.OrderBy(r => r.Receipt.Date).ThenBy(r => r.Receipt.Number).ThenBy(r => r.Line.Sort)) {
            var open = r.Line.BaseQuantity - outflow.GetValueOrDefault((r.Line.Id, r.Line.WarehouseId));
            if (open <= 0m) continue;
            var factor = ReceiptLineStock.Factor(r.Line);
            var info   = products.GetValueOrDefault(r.Line.ProductId);
            result.Add(new QuarantineRowDto(r.Line.Id, r.Receipt.Id, r.Receipt.Number, r.Receipt.Date, suppliers.GetValueOrDefault(r.Receipt.SupplierId)?.Name ?? string.Empty,
                                            r.Receipt.PurchaseOrderId, r.Line.ProductId, info?.Code ?? string.Empty, info?.Name ?? string.Empty, r.Line.VariantId, r.Line.Unit,
                                            r.Line.Quantity, Math.Round(open / factor, 6, MidpointRounding.AwayFromZero), open, r.Line.WarehouseId, r.Line.TargetWarehouseId));
        }

        return result;
    }
}

public sealed record ReleaseInput(Guid ReceiptLineId, decimal Quantity, Guid TargetWarehouseId);

public sealed record RejectInput(Guid ReceiptLineId, decimal Quantity, string Action);

internal static class QuarantineSupport {
    public static async Task EnsureEnabledAsync(ITenantSettings settings, CancellationToken ct) {
        if (!(await settings.GetAsync<PurchasingSettings>(ct)).QuarantineEnabled) throw PurchasingErrors.QuarantineNotEnabled();
    }

    public static DateOnly Today(TimeProvider time, DateOnly receiptDate) {
        var today = CurrentAccountClock.Today(time);
        return today < receiptDate ? receiptDate : today;
    }

    public static async Task<GoodsReceipt> LoadByLineAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid lineId, bool track, CancellationToken ct) {
        var receiptId = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => (Guid?)l.GoodsReceiptId).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException(lineId.ToString(), "GoodsReceiptLine");
        return await ReceiptRules.LoadAsync(db, tenant, receiptId, track, ct);
    }

    public static void EnsureHeld(GoodsReceipt receipt, GoodsReceiptLine line) {
        if (receipt.Status != GoodsReceiptStatus.Posted) throw PurchasingErrors.Conflict("receipt_cancelled", "İptal edilmiş mal kabul için karantina işlemi yapılamaz.");
        if (line.Quarantine != QuarantineState.Held) throw PurchasingErrors.Conflict("quarantine_not_held", "Bu satır karantinada değil.");
    }
}

/// <summary>
///     Karantinadan serbest bırakma: açık miktar (kilitten sonra hesaplanır) hedef normal depoya P3 transferiyle taşınır. Siparişli kabulde
///     bekleyen rezervasyonlar (kabulde karantina nedeniyle taşınmamıştı) şimdi hedef depoya geçer. Miktar satır biriminde verilir.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Quarantine)]
public sealed record ReleaseQuarantineCommand(ReleaseInput Input) : IRequest<QuarantineResultDto>;

public sealed class ReleaseQuarantineCommandValidator : AbstractValidator<ReleaseQuarantineCommand> {
    public ReleaseQuarantineCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceiptLineId).NotEmpty().WithMessage("Mal kabul satırı seçilmelidir.");
        RuleFor(c => c.Input.TargetWarehouseId).NotEmpty().WithMessage("Hedef depo seçilmelidir.");
        RuleFor(c => c.Input.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.").Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
    }
}

public sealed class ReleaseQuarantineCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    ReceiptLineStock      lineStock,
    ITenantSettings       settings,
    IOutbox               outbox,
    TimeProvider          time
) : IRequestHandler<ReleaseQuarantineCommand, QuarantineResultDto> {
    public async Task<QuarantineResultDto> Handle(ReleaseQuarantineCommand request, CancellationToken ct) {
        var input = request.Input;
        await QuarantineSupport.EnsureEnabledAsync(settings, ct);

        var head = await QuarantineSupport.LoadByLineAsync(db, tenant, input.ReceiptLineId, false, ct);
        var peek = head.Lines.First(l => l.Id == input.ReceiptLineId);
        QuarantineSupport.EnsureHeld(head, peek);
        var target = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == input.TargetWarehouseId, ct) ?? throw new NotFoundException(input.TargetWarehouseId.ToString(), "Warehouse");
        BranchGuard.Ensure(tenant, target.BranchId);
        if (target.Kind != WarehouseKind.Normal) throw PurchasingErrors.Bad("target_not_normal", "Hedef depo normal bir depo olmalıdır.");
        if (target.BranchId != head.BranchId) throw PurchasingErrors.Bad("warehouse_branch_mismatch", "Depo, mal kabulün şubesine ait değil.");
        if (!target.IsActive) throw PurchasingErrors.Conflict("warehouse_inactive", "Depo pasif.");

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == peek.ProductId, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        // Tüm stok kilitleri tek seferde: karantina deposu, hedef depo, takipli kalem kilidi ve bekleyen rezervasyonların depoları.
        var keys = PurchaseReturnService.KeysFor([peek], new Dictionary<Guid, ERP.Domain.Catalog.Product> { [product.Id] = product }).ToList();
        keys.Add((target.Id, peek.ProductId, peek.VariantId));
        if (head.PurchaseOrderId is { } poId)
            keys.AddRange((await db.Reservations.AsNoTracking()
                                   .Where(r => r.ExpectedReceiptSourceId == poId && r.Status == ReservationStatus.Active && r.ProductId == peek.ProductId && r.VariantId == peek.VariantId)
                                   .Select(r => new { r.WarehouseId, r.ProductId, r.VariantId }).Distinct().ToListAsync(ct))
                          .Select(r => (r.WarehouseId, r.ProductId, r.VariantId)));
        await db.LockStockAsync(keys, ct);

        var receipt = await ReceiptRules.LoadAsync(db, tenant, head.Id, true, ct);
        var line    = receipt.Lines.First(l => l.Id == input.ReceiptLineId);
        QuarantineSupport.EnsureHeld(receipt, line);

        var baseQty = Math.Round(input.Quantity * ReceiptLineStock.Factor(line), 6, MidpointRounding.AwayFromZero);
        var open    = await lineStock.OpenAsync(line, ct);
        if (baseQty > open) throw PurchasingErrors.Conflict("quarantine_exceeds_open", "Miktar karantinadaki açık miktarı aşıyor.");

        var (lots, serials) = await lineStock.DeriveAsync(line, product, baseQty, ct);
        var date            = QuarantineSupport.Today(time, receipt.Date);
        await stock.TransferAsync(ReceiptLineStock.ReleaseSource, Guid.CreateVersion7(), date,
                                  [new TransferLine(line.WarehouseId, target.Id, line.ProductId, line.VariantId, baseQty, lots, serials, line.Id)], ct);
        line.TargetWarehouseId = target.Id;
        if (receipt.PurchaseOrderId is { } expectedId)
            await stock.RetargetExpectedAsync(expectedId, target.Id, baseQty, ct, line.ProductId, line.VariantId);

        await lineStock.CloseIfResolvedAsync(line, ct);
        outbox.Enqueue("purchase.quarantine-released", new {
            poId = receipt.PurchaseOrderId, receiptId = receipt.Id, receiptLineId = line.Id, productId = line.ProductId, variantId = line.VariantId,
            baseQuantity = baseQty, targetWarehouseId = target.Id
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new QuarantineResultDto(line.Id, line.Quarantine, Math.Round(await lineStock.OpenAsync(line, ct) / ReceiptLineStock.Factor(line), 6), null, null, null);
    }
}

/// <summary>
///     Karantinadan red: "damaged" şubenin hasarlı deposuna transfer eder; "return" aynı miktarı tedarikçiye iade eder (stok çıkışı +
///     varsa alış faturasına bağlı iade faturası taslağı). Miktar satır biriminde verilir.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Quarantine)]
public sealed record RejectQuarantineCommand(RejectInput Input) : IRequest<QuarantineResultDto>;

public sealed class RejectQuarantineCommandValidator : AbstractValidator<RejectQuarantineCommand> {
    public RejectQuarantineCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceiptLineId).NotEmpty().WithMessage("Mal kabul satırı seçilmelidir.");
        RuleFor(c => c.Input.Action).Must(a => a is "damaged" or "return").WithMessage("İşlem \"damaged\" ya da \"return\" olmalıdır.");
        RuleFor(c => c.Input.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.").Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
    }
}

public sealed class RejectQuarantineCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IStockService         stock,
    ReceiptLineStock      lineStock,
    PurchaseReturnService returns,
    ITenantSettings       settings,
    IOutbox               outbox,
    TimeProvider          time
) : IRequestHandler<RejectQuarantineCommand, QuarantineResultDto> {
    public async Task<QuarantineResultDto> Handle(RejectQuarantineCommand request, CancellationToken ct) {
        var input = request.Input;
        await QuarantineSupport.EnsureEnabledAsync(settings, ct);

        var head = await QuarantineSupport.LoadByLineAsync(db, tenant, input.ReceiptLineId, false, ct);
        var peek = head.Lines.First(l => l.Id == input.ReceiptLineId);
        QuarantineSupport.EnsureHeld(head, peek);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == peek.ProductId, ct);

        Warehouse? damaged = null;
        if (input.Action == "damaged") {
            damaged = await db.Warehouses.AsNoTracking().Where(w => w.BranchId == head.BranchId && w.Kind == WarehouseKind.Damaged && w.IsActive).OrderBy(w => w.Code).FirstOrDefaultAsync(ct)
                      ?? throw PurchasingErrors.Conflict("damaged_warehouse_missing", "Bu şubede hasarlı deposu tanımlı değil.");
            BranchGuard.Ensure(tenant, damaged.BranchId);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        var keys = PurchaseReturnService.KeysFor([peek], new Dictionary<Guid, ERP.Domain.Catalog.Product> { [product.Id] = product }).ToList();
        if (damaged is not null) keys.Add((damaged.Id, peek.ProductId, peek.VariantId));
        await db.LockStockAsync(keys, ct);

        var receipt = await ReceiptRules.LoadAsync(db, tenant, head.Id, true, ct);
        var line    = receipt.Lines.First(l => l.Id == input.ReceiptLineId);
        QuarantineSupport.EnsureHeld(receipt, line);

        var baseQty = Math.Round(input.Quantity * ReceiptLineStock.Factor(line), 6, MidpointRounding.AwayFromZero);
        var open    = await lineStock.OpenAsync(line, ct);
        if (baseQty > open) throw PurchasingErrors.Conflict("quarantine_exceeds_open", "Miktar karantinadaki açık miktarı aşıyor.");

        var date = QuarantineSupport.Today(time, receipt.Date);
        Guid? returnId = null, invoiceId = null;
        string? invoiceNumber = null;
        if (damaged is not null) {
            var (lots, serials) = await lineStock.DeriveAsync(line, product, baseQty, ct);
            await stock.TransferAsync(ReceiptLineStock.RejectSource, Guid.CreateVersion7(), date,
                                      [new TransferLine(line.WarehouseId, damaged.Id, line.ProductId, line.VariantId, baseQty, lots, serials, line.Id)], ct);
            await lineStock.CloseIfResolvedAsync(line, ct);
        } else {
            var outcome = await returns.ReturnAsync(receipt, [new ReturnLineRequest(line.Id, input.Quantity, null, null)], date, "Karantina reddi", true, true, ct, invoiceOptional: true);
            (returnId, invoiceId, invoiceNumber) = (outcome.ReturnId, outcome.InvoiceId, outcome.InvoiceNumber);
        }

        outbox.Enqueue("purchase.quarantine-rejected", new {
            poId = receipt.PurchaseOrderId, receiptId = receipt.Id, receiptLineId = line.Id, productId = line.ProductId, variantId = line.VariantId,
            baseQuantity = baseQty, action = input.Action
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new QuarantineResultDto(line.Id, line.Quarantine, Math.Round(await lineStock.OpenAsync(line, ct) / ReceiptLineStock.Factor(line), 6), returnId, invoiceId, invoiceNumber);
    }
}
