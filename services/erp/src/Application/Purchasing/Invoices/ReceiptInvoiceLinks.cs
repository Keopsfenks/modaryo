using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Invoices;

/// <summary>Fatura satırı ile mal kabul satırı arasındaki bağ.</summary>
public sealed record InvoiceLink(InvoiceLine Line, GoodsReceipt Receipt, GoodsReceiptLine ReceiptLine);

/// <summary>
///     Mal kabulden kesilen alış faturalarının bağı: fatura kaynağı "purchase-receipt", kaynak kimliği mal kabul (birden çok mal kabul
///     birleştirildiyse ilki); satır bağı fatura satırının SourceLineId alanındaki mal kabul satırıdır. Satır bağı olmayan satırlar
///     kaynak mal kabulün aynı kalemli satırına eşlenir.
/// </summary>
public static class ReceiptInvoiceLinks {
    public const string Source = "purchase-receipt";

    public static async Task<List<InvoiceLink>> ResolveAsync(IApplicationDbContext db, Invoice invoice, bool track, CancellationToken ct) {
        var result = new List<InvoiceLink>();
        if (invoice.Kind != InvoiceKind.Purchase || invoice.SourceType != Source) return result;

        var lineIds = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToList();
        var lines   = track ? db.GoodsReceiptLines.AsQueryable() : db.GoodsReceiptLines.AsNoTracking();
        var direct  = await lines.Where(g => lineIds.Contains(g.Id)).ToListAsync(ct);
        var pool    = invoice.SourceId is { } sid ? await lines.Where(g => g.GoodsReceiptId == sid).ToListAsync(ct) : [];

        var receiptIds = direct.Select(g => g.GoodsReceiptId).Concat(pool.Select(g => g.GoodsReceiptId)).Distinct().ToList();
        var receipts   = await db.GoodsReceipts.AsNoTracking().Where(r => receiptIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var used = new HashSet<Guid>();
        foreach (var line in invoice.Lines.OrderBy(l => l.Sort)) {
            var match = line.SourceLineId is { } id ? direct.FirstOrDefault(g => g.Id == id) : null;
            if (match is null && line.ProductId is { } product && line.SourceLineId is null)
                match = pool.FirstOrDefault(g => !used.Contains(g.Id) && g.ProductId == product && g.VariantId == line.VariantId
                                                 && !invoice.Lines.Any(o => o.SourceLineId == g.Id));
            if (match is null) continue;
            used.Add(match.Id);
            result.Add(new InvoiceLink(line, receipts[match.GoodsReceiptId], match));
        }

        return result;
    }

    /// <summary>Verilen mal kabullere bağlı, iptal edilmemiş alış faturaları (satırlarıyla, izlemesiz).</summary>
    public static async Task<List<Invoice>> InvoicesAsync(IApplicationDbContext db, IReadOnlyCollection<Guid> receiptIds, CancellationToken ct) {
        var lineIds = await db.GoodsReceiptLines.AsNoTracking().Where(g => receiptIds.Contains(g.GoodsReceiptId)).Select(g => g.Id).ToListAsync(ct);
        return await db.Invoices.AsNoTracking().Include(i => i.Lines)
                       .Where(i => i.SourceType == Source && i.Kind == InvoiceKind.Purchase && i.Status != InvoiceStatus.Cancelled
                                   && ((i.SourceId != null && receiptIds.Contains(i.SourceId.Value))
                                       || i.Lines.Any(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))))
                       .OrderBy(i => i.Date).ThenBy(i => i.Created).ToListAsync(ct);
    }
}
