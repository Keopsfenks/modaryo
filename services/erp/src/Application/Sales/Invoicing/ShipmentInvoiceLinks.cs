using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Invoicing;

/// <summary>
///     Sevkten kesilen satış faturalarının bağı: fatura kaynağı "sales-shipment", kaynak kimliği ilk sevk; satır bağı fatura satırının
///     SourceLineId alanındaki sevk satırıdır. Faturalanan miktar (taslaklar ve kesilmişler dahil, iptal edilmemiş) sipariş kilidi altında sayılır.
/// </summary>
public static class ShipmentInvoiceLinks {
    public const string Source = "sales-shipment";

    private const decimal Tolerance = 0.001m;

    public static bool IsLinked(Invoice invoice) {
        return invoice.Kind == InvoiceKind.Sales && invoice.SourceType == Source;
    }

    /// <summary>Sevk satırı başına faturalanan temel miktar (iptal edilmemiş faturalar; exceptInvoiceId sayım dışıdır).</summary>
    public static async Task<Dictionary<Guid, decimal>> InvoicedBaseAsync(IApplicationDbContext db, IReadOnlyCollection<Guid> shipmentLineIds, Guid? exceptInvoiceId,
                                                                          CancellationToken ct
    ) {
        var rows = await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && shipmentLineIds.Contains(l.SourceLineId.Value))
                           .Join(db.Invoices.Where(i => i.SourceType == Source && i.Status != InvoiceStatus.Cancelled && (exceptInvoiceId == null || i.Id != exceptInvoiceId)),
                                 l => l.InvoiceId, i => i.Id, (l, _) => new { Source = l.SourceLineId!.Value, l.Quantity, l.Factor }).ToListAsync(ct);
        return rows.GroupBy(r => r.Source).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity * r.Factor));
    }

    public static async Task<bool> IsInvoicedAsync(IApplicationDbContext db, IReadOnlyCollection<Guid> shipmentLineIds, CancellationToken ct) {
        return await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && shipmentLineIds.Contains(l.SourceLineId.Value))
                       .Join(db.Invoices.Where(i => i.SourceType == Source && i.Status != InvoiceStatus.Cancelled), l => l.InvoiceId, i => i.Id, (l, _) => l.Id).AnyAsync(ct);
    }

    /// <summary>
    ///     Verilen bağlı satırların miktarı (diğer faturalarla birlikte) sevk miktarını aşarsa 409 verir. Çağıran sipariş kilidini önceden almış olmalıdır.
    ///     <paramref name="own" /> bu faturanın ya da taslağın temel miktarlarıdır (exceptInvoiceId olarak kendisi sayım dışı bırakılır).
    /// </summary>
    public static async Task EnsureNotOverInvoicedAsync(IApplicationDbContext db, Guid? exceptInvoiceId, IEnumerable<(Guid ShipmentLineId, decimal BaseQuantity)> own,
                                                        CancellationToken ct
    ) {
        var total = new Dictionary<Guid, decimal>();
        foreach (var (lineId, quantity) in own) total[lineId] = total.GetValueOrDefault(lineId) + quantity;
        if (total.Count == 0) return;

        foreach (var (lineId, quantity) in await InvoicedBaseAsync(db, total.Keys.ToList(), exceptInvoiceId, ct)) total[lineId] += quantity;

        var lines = await db.ShipmentLines.AsNoTracking().Where(l => total.Keys.Contains(l.Id)).ToListAsync(ct);
        foreach (var line in lines) {
            var shipped = line.Quantity * line.Factor;
            if (total[line.Id] > shipped + Tolerance)
                throw SalesErrors.Conflict("shipment_over_invoiced",
                                           $"\"{line.Description}\" için faturalanan miktar ({Math.Round(total[line.Id], 6):0.######}) sevk edilen miktarı ({shipped:0.######}) aşıyor.");
        }
    }
}

/// <summary>Sevkten kesilen taslak faturanın sabit alanları ve miktar denetimi (güncelleme yolunda).</summary>
public sealed class ShipmentInvoiceGuard(IApplicationDbContext db) {
    /// <summary>Taslak güncellemesinde sabit alanların değişmediğini denetler (kilit gerekmez).</summary>
    public static void EnsureDraftFieldsLocked(Invoice invoice, InvoiceInput input) {
        if (input.Kind != invoice.Kind)
            throw InvoiceErrors.Bad("sales_invoice_locked", "Sevkten kesilen faturanın türü değiştirilemez.");
        if (input.AffectsStock)
            throw InvoiceErrors.Bad("sales_invoice_affects_stock", "Sevkten kesilen faturada \"Stoğa işle\" açılamaz; stok sevkte çıkmıştır.");
        if (input.CurrentAccountId != invoice.CurrentAccountId)
            throw InvoiceErrors.Bad("sales_invoice_locked", "Sevkten kesilen faturanın müşterisi değiştirilemez.");
        if (input.BranchId != invoice.BranchId)
            throw InvoiceErrors.Bad("sales_invoice_locked", "Sevkten kesilen faturanın şubesi değiştirilemez.");
        if (!string.Equals(input.Currency?.Trim(), invoice.Currency, StringComparison.OrdinalIgnoreCase))
            throw InvoiceErrors.Bad("sales_invoice_locked", "Sevkten kesilen faturanın para birimi değiştirilemez.");

        // Her satır bir sevk satırına bağlı kalmalıdır: bağsız satır faturalanan miktar sayımına girmez ve aynı sevk ikinci kez faturalanabilirdi.
        var allowed = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToHashSet();
        if (input.Lines.Any(l => l.SourceLineId is not { } id || !allowed.Contains(id)))
            throw InvoiceErrors.Bad("sales_invoice_locked", "Sevkten kesilen faturanın her satırı bir sevk satırına bağlı kalmalıdır; bağlar değiştirilemez.");
    }

    /// <summary>Faturanın bağlı olduğu siparişin kimliği (kilit için).</summary>
    public async Task<Guid?> OrderIdAsync(Invoice invoice, CancellationToken ct) {
        var lineIds = invoice.Lines.Where(l => l.SourceLineId != null).Select(l => l.SourceLineId!.Value).ToList();
        if (lineIds.Count == 0) return null;
        return await db.ShipmentLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
                       .Join(db.Shipments, l => l.ShipmentId, s => s.Id, (l, s) => (Guid?)s.SalesOrderId).FirstOrDefaultAsync(ct);
    }
}
