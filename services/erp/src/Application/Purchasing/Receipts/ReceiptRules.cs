using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Receipts;

public sealed class ReceiptInputValidator : AbstractValidator<ReceiptInput> {
    public ReceiptInputValidator() {
        RuleFor(i => i.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        RuleFor(i => i.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
        RuleFor(i => i.Date).Must(d => d != default).WithMessage("Mal kabul tarihi zorunludur.");
        RuleFor(i => i.DeliveryNoteNo).MaximumLength(60).WithMessage("İrsaliye numarası en fazla 60 karakter olabilir.");
        RuleFor(i => i.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
        RuleFor(i => i.Currency).Must(c => c is null || InvoiceRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi TRY, USD, EUR ya da GBP olmalıdır.");
        RuleFor(i => i.Lines).NotEmpty().WithMessage("Mal kabulde en az bir satır olmalıdır.")
            .Must(l => l is null || l.Count <= 500).WithMessage("Mal kabulde en fazla 500 satır olabilir.");
        RuleForEach(i => i.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.")
                .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).When(x => x.UnitPrice is not null).WithMessage("Birim fiyat negatif olamaz.")
                .Must(p => p is null || Math.Round(p.Value, 6) == p).WithMessage("Birim fiyat en fazla 6 ondalık basamak içerebilir.");
        });
    }
}

public static class ReceiptRules {
    public const string StockSource = "purchase-receipt";

    /// <summary>
    ///     Mal kabul ile fatura bağı: fatura kaynağı "purchase-receipt" ve kaynak kimliği mal kabuldür (alış faturası, iptal edilmemiş).
    ///     Alış iade faturaları bu denetime girmez.
    /// </summary>
    public const string InvoiceSource = "purchase-receipt";

    public static async Task<GoodsReceipt> LoadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, bool track, CancellationToken ct) {
        var query = db.GoodsReceipts.Include(r => r.Lines).AsQueryable();
        if (!track) query = query.AsNoTracking();
        var receipt = await query.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "GoodsReceipt");
        BranchGuard.Ensure(tenant, receipt.BranchId);
        return receipt;
    }

    public static Task<bool> IsInvoicedAsync(IApplicationDbContext db, Guid receiptId, CancellationToken ct) {
        return db.Invoices.AnyAsync(i => i.SourceType == InvoiceSource && i.SourceId == receiptId && i.Kind == InvoiceKind.Purchase
                                         && i.Status != InvoiceStatus.Cancelled, ct);
    }

    /// <summary>Sipariş durumunu satırların gelen ve kapatılan miktarlarından yeniden hesaplar (kapalı/iptal siparişe dokunmaz).</summary>
    public static void RecomputeStatus(PurchaseOrder po) {
        if (po.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)) return;
        if (po.Lines.All(l => l.ReceivedQuantity <= 0m)) po.Status = PurchaseOrderStatus.Approved;
        else if (po.Lines.Any(l => l.OpenQuantity > 0m)) po.Status = PurchaseOrderStatus.PartiallyReceived;
        else po.Status = PurchaseOrderStatus.Received;
    }
}

/// <summary>Mal kabulü DTO'ya çevirir. Alış fiyatı yetkisi yoksa tüm fiyat ve tutar alanları null döner.</summary>
public sealed class ReceiptMapper(
    IApplicationDbContext db,
    ICurrentSession       session
) {
    public async Task<ReceiptDto> ToDtoAsync(GoodsReceipt r, IReadOnlyList<string> warnings, CancellationToken ct) {
        var showPrices = session.HasPermission(PurchasingPermissions.PurchasePrice);
        var supplier   = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == r.SupplierId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var poNumber   = r.PurchaseOrderId is { } poId
                             ? await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == poId).Select(p => p.Number).FirstOrDefaultAsync(ct)
                             : null;
        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var invoiced   = await ReceiptRules.IsInvoicedAsync(db, r.Id, ct);

        var lines = r.Lines.OrderBy(l => l.Sort).Select(l => {
            var info = products.GetValueOrDefault(l.ProductId);
            return new ReceiptLineDto(l.Id, l.Sort, l.PurchaseOrderLineId, l.ProductId, info?.Code ?? string.Empty, info?.Name ?? string.Empty, l.VariantId, l.Unit,
                                      l.Quantity, l.BaseQuantity, showPrices ? l.UnitPrice : null, showPrices ? l.AdjustedUnitCostTl ?? l.UnitCostTl : null,
                                      l.WarehouseId, l.Quarantine, l.TargetWarehouseId);
        }).ToList();

        decimal? total = showPrices ? r.Lines.Sum(l => Math.Round((l.UnitPrice ?? 0m) * l.Quantity, 2, MidpointRounding.AwayFromZero)) : null;
        return new ReceiptDto(r.Id, r.Number, r.Status, r.PurchaseOrderId, poNumber, r.SupplierId, supplier, r.BranchId, r.WarehouseId, r.Date, r.Currency, r.Rate,
                              r.SupplierDeliveryNo, r.Notes, r.CancelReason, total, invoiced, warnings, lines);
    }
}
