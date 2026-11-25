using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Invoices;

/// <summary>
///     Mal kabule bağlı alış faturası kuralları: taslakta stok etkisi, tedarikçi, şube, para birimi ve mal kabul satırı bağları sabittir;
///     bir mal kabul satırı için faturalanan miktar (taslaklar ve kesilmiş faturalar dahil) mal kabul miktarını aşamaz. Aşım denetimi
///     mal kabul kilidi (<see cref="IApplicationDbContext.LockReceiptsAsync" />) altında yapılır.
/// </summary>
public sealed class ReceiptInvoiceGuard(IApplicationDbContext db) {
    private const decimal Tolerance = 0.001m;

    public static bool IsReceiptLinked(Invoice invoice) {
        return invoice.Kind == InvoiceKind.Purchase && invoice.SourceType == ReceiptInvoiceLinks.Source;
    }

    /// <summary>Taslak güncellemesinde sabit alanların değişmediğini denetler (kilit gerekmez).</summary>
    public static void EnsureDraftFieldsLocked(Invoice invoice, InvoiceInput input) {
        if (input.Kind != invoice.Kind)
            throw InvoiceErrors.Bad("receipt_invoice_locked", "Mal kabulden kesilen faturanın türü değiştirilemez.");
        if (input.AffectsStock)
            throw InvoiceErrors.Bad("receipt_invoice_affects_stock", "Mal kabulden kesilen faturada \"Stoğa işle\" açılamaz; stok mal kabulde girmiştir.");
        if (input.CurrentAccountId != invoice.CurrentAccountId)
            throw InvoiceErrors.Bad("receipt_invoice_locked", "Mal kabulden kesilen faturanın tedarikçisi değiştirilemez.");
        if (input.BranchId != invoice.BranchId)
            throw InvoiceErrors.Bad("receipt_invoice_locked", "Mal kabulden kesilen faturanın şubesi değiştirilemez.");
        if (!string.Equals(input.Currency?.Trim(), invoice.Currency, StringComparison.OrdinalIgnoreCase))
            throw InvoiceErrors.Bad("receipt_invoice_locked", "Mal kabulden kesilen faturanın para birimi değiştirilemez.");

        var allowed = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToHashSet();
        if (input.Lines.Any(l => l.SourceLineId is { } id && !allowed.Contains(id)))
            throw InvoiceErrors.Bad("receipt_invoice_locked", "Mal kabulden kesilen faturanın mal kabul satırı bağları değiştirilemez.");
    }

    /// <summary>Faturanın bağlı olduğu mal kabul kimlikleri (satır bağları ve kaynak mal kabul).</summary>
    public async Task<List<Guid>> ReceiptIdsAsync(Invoice invoice, CancellationToken ct) {
        var lineIds = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToList();
        var ids     = await db.GoodsReceiptLines.AsNoTracking().Where(g => lineIds.Contains(g.Id)).Select(g => g.GoodsReceiptId).Distinct().ToListAsync(ct);
        if (invoice.SourceId is { } source && !ids.Contains(source)) ids.Add(source);
        return ids;
    }

    /// <summary>
    ///     Faturalanan temel miktarı (bu fatura hariç, iptal edilmemiş faturalar) ile verilen sahip miktarlarını toplar; mal kabul satırı
    ///     miktarını aşarsa 409 verir. Çağıran mal kabul kilidini önceden almış olmalıdır.
    /// </summary>
    public async Task EnsureNotOverInvoicedAsync(IReadOnlyCollection<Guid> receiptIds, Guid? exceptInvoiceId, IEnumerable<(Guid ReceiptLineId, decimal BaseQuantity)> own,
                                                 CancellationToken ct
    ) {
        var total = new Dictionary<Guid, decimal>();
        foreach (var (lineId, quantity) in own) total[lineId] = total.GetValueOrDefault(lineId) + quantity;
        if (total.Count == 0) return;

        foreach (var other in (await ReceiptInvoiceLinks.InvoicesAsync(db, receiptIds, ct)).Where(i => i.Id != exceptInvoiceId))
            foreach (var link in await ReceiptInvoiceLinks.ResolveAsync(db, other, false, ct))
                if (total.ContainsKey(link.ReceiptLine.Id))
                    total[link.ReceiptLine.Id] += link.Line.Quantity * link.Line.Factor;

        var lines = await db.GoodsReceiptLines.AsNoTracking().Where(g => total.Keys.Contains(g.Id)).ToListAsync(ct);
        foreach (var line in lines)
            if (total[line.Id] > line.BaseQuantity + Tolerance)
                throw PurchasingErrors.Conflict("receipt_over_invoiced",
                                                $"Mal kabul satırı için faturalanan miktar ({Math.Round(total[line.Id], 6):0.######}) mal kabul miktarını ({line.BaseQuantity:0.######}) aşıyor.");
    }

    /// <summary>
    ///     Fatura kesiminde: stok etkisi kapalı, tedarikçi, şube ve para birimi bağlı mal kabullerle aynı, mal kabuller iptal edilmemiş
    ///     ve faturalanan miktar aşılmamış olmalıdır. Mal kabul kilidi burada alınır.
    /// </summary>
    public async Task EnsureIssuableAsync(Invoice invoice, CancellationToken ct) {
        if (invoice.AffectsStock)
            throw InvoiceErrors.Conflict("receipt_invoice_affects_stock", "Mal kabulden kesilen fatura stoğa işlenemez; stok mal kabulde girmiştir.");

        await db.LockReceiptsAsync(await ReceiptIdsAsync(invoice, ct), ct);

        var links = await ReceiptInvoiceLinks.ResolveAsync(db, invoice, false, ct);
        foreach (var receipt in links.Select(l => l.Receipt).DistinctBy(r => r.Id)) {
            if (receipt.Status == GoodsReceiptStatus.Cancelled)
                throw InvoiceErrors.Conflict("receipt_cancelled", $"{receipt.Number} numaralı mal kabul iptal edilmiş; fatura kesilemez.");
            if (receipt.SupplierId != invoice.CurrentAccountId)
                throw InvoiceErrors.Conflict("receipt_invoice_locked", $"{receipt.Number} numaralı mal kabul başka bir tedarikçiye ait.");
            if (receipt.BranchId != invoice.BranchId)
                throw InvoiceErrors.Conflict("receipt_invoice_locked", $"{receipt.Number} numaralı mal kabul başka bir şubeye ait.");
            if (!string.Equals(receipt.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
                throw InvoiceErrors.Conflict("receipt_invoice_locked", $"{receipt.Number} numaralı mal kabulün para birimi faturadan farklı.");
        }

        await EnsureNotOverInvoicedAsync(links.Select(l => l.Receipt.Id).Distinct().ToList(), invoice.Id,
                                         links.Select(l => (l.ReceiptLine.Id, l.Line.Quantity * l.Line.Factor)), ct);
    }
}
