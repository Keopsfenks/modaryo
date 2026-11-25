using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Application.Invoicing.Posting;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Invoices;

/// <summary>
///     Mal kabul maliyeti ile fatura maliyeti arasındaki farkı P3 maliyet düzeltmesi (Revaluation hareketi + yeniden maliyetlendirme işi)
///     olarak mal kabul tarihine yazar. Satır TL maliyeti = (net TL + ÖTV TL) / temel birim miktarı. Kur farkı da buna dahildir:
///     fatura kuru ile mal kabul kuru arasındaki fark maliyete yansır. Stok kilitleri çağıran tarafından (InvoicePoster) tek seferde alınır.
/// </summary>
public sealed class CostCorrection(IApplicationDbContext db, IStockService stock, IPeriodLock periodLock) {
    public const string SourceType = "purchase-invoice-correction";

    private const string ReceiptStockSource = "purchase-receipt";

    private sealed record Item(InvoiceLink Link, StockMovement Inbound, decimal Delta);

    private async Task<List<Item>> PlanAsync(Invoice invoice, bool track, CancellationToken ct) {
        var items = new List<Item>();
        foreach (var link in await ReceiptInvoiceLinks.ResolveAsync(db, invoice, track, ct)) {
            var invoiceBase = Math.Round(link.Line.Quantity * link.Line.Factor, 6, MidpointRounding.AwayFromZero);
            if (invoiceBase <= 0m || link.Line.ProductId is null) continue;

            var inbound = await db.StockMovements.AsNoTracking()
                                  .Where(m => m.SourceType == ReceiptStockSource && m.SourceId == link.Receipt.Id && m.SourceLineId == link.ReceiptLine.Id && m.Quantity > 0m)
                                  .OrderBy(m => m.Sequence).FirstOrDefaultAsync(ct);
            if (inbound is null) continue;

            var invoiceTotal = link.Line.NetTl + link.Line.OtvTl;
            var receiptUnit  = link.ReceiptLine.UnitCostTl ?? inbound.UnitCost;
            var quantity     = Math.Min(invoiceBase, link.ReceiptLine.BaseQuantity);
            var delta        = Math.Round(invoiceTotal / invoiceBase * quantity - receiptUnit * quantity, 2, MidpointRounding.AwayFromZero);
            if (delta != 0m) items.Add(new Item(link, inbound, delta));
        }

        return items;
    }

    /// <summary>Bir kaynağın yazdığı düzeltmelerin stok anahtarları (dönem kilidi de önceden denetlenir).</summary>
    public async Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> ReverseKeysAsync(string sourceType, Guid sourceId, CancellationToken ct) {
        var moved = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == sourceType && m.SourceId == sourceId && m.Type == MovementType.Revaluation)
                            .Select(m => new { m.WarehouseId, m.ProductId, m.VariantId, m.Date }).ToListAsync(ct);
        foreach (var date in moved.Select(m => m.Date).Distinct()) await periodLock.EnsureOpenAsync(date, ct);
        return moved.Select(m => (m.WarehouseId, m.ProductId, m.VariantId)).Distinct().ToList();
    }

    public async Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> KeysAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        if (cancelling) return await ReverseKeysAsync(SourceType, invoice.Id, ct);

        var plan = await PlanAsync(invoice, false, ct);
        foreach (var date in plan.Select(p => p.Link.Receipt.Date).Distinct()) await periodLock.EnsureOpenAsync(date, ct);
        return plan.Select(p => (p.Inbound.WarehouseId, p.Inbound.ProductId, p.Inbound.VariantId)).Distinct().ToList();
    }

    public async Task ApplyAsync(Invoice invoice, CancellationToken ct) {
        foreach (var item in await PlanAsync(invoice, true, ct))
            await CorrectAsync(item.Inbound, item.Link.ReceiptLine, item.Link.Receipt.Date, item.Delta, SourceType, invoice.Id, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Giriş hareketini P3 maliyet düzeltmesiyle (Revaluation + yeniden maliyetlendirme işi) düzeltir ve mal kabul satırının düzeltilmiş maliyetini günceller.</summary>
    public async Task CorrectAsync(StockMovement inbound, GoodsReceiptLine receiptLine, DateOnly date, decimal delta, string sourceType, Guid sourceId, CancellationToken ct) {
        await stock.CorrectCostAsync(inbound.WarehouseId, inbound.ProductId, inbound.VariantId, date, inbound.Id, delta, sourceType, sourceId, ct);
        Adjust(receiptLine, delta);
    }

    public Task ReverseAsync(Invoice invoice, CancellationToken ct) {
        return ReverseAsync(SourceType, invoice.Id, ct);
    }

    /// <summary>Bir kaynağın yazdığı düzeltmelerin net tutarını giriş hareketi başına ters yönde yazar.</summary>
    public async Task ReverseAsync(string sourceType, Guid sourceId, CancellationToken ct) {
        var written = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == sourceType && m.SourceId == sourceId && m.Type == MovementType.Revaluation)
                              .OrderBy(m => m.Sequence).ToListAsync(ct);
        const string prefix = "correction:";
        foreach (var group in written.GroupBy(m => m.Note)) {
            var net = group.Sum(m => m.TotalCost);
            if (net == 0m || group.Key is null || !group.Key.StartsWith(prefix) || !Guid.TryParse(group.Key[prefix.Length..], out var inboundId)) continue;

            var first = group.First();
            await stock.CorrectCostAsync(first.WarehouseId, first.ProductId, first.VariantId, first.Date, inboundId, -net, sourceType, sourceId, ct);
            var lineId = await db.StockMovements.AsNoTracking().Where(m => m.Id == inboundId).Select(m => m.SourceLineId).FirstOrDefaultAsync(ct);
            if (lineId is { } id && await db.GoodsReceiptLines.FirstOrDefaultAsync(g => g.Id == id, ct) is { } receiptLine) Adjust(receiptLine, -net);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void Adjust(GoodsReceiptLine line, decimal deltaTotal) {
        if (line.BaseQuantity <= 0m) return;
        var current = line.AdjustedUnitCostTl ?? line.UnitCostTl ?? 0m;
        line.AdjustedUnitCostTl = Math.Round(current + deltaTotal / line.BaseQuantity, 6, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Mal kabulden kesilen alış faturası kesilince / iptal edilince maliyet düzeltmesini yazar ya da geri alır.</summary>
public sealed class PurchaseInvoiceHook(CostCorrection correction, IApplicationDbContext db) : IInvoicePostHook {
    private static bool Applies(Invoice invoice) {
        return invoice.Kind == InvoiceKind.Purchase && invoice.SourceType == ReceiptInvoiceLinks.Source;
    }

    public async Task<IReadOnlyList<(Guid WarehouseId, Guid ProductId, Guid? VariantId)>> StockKeysAsync(Invoice invoice, bool cancelling, CancellationToken ct) {
        // Masraf dağıtımı kaynağı olan fatura, dağıtım işlenmişken iptal edilemez (iptal kilit planında, hiçbir şey yazılmadan reddedilir).
        if (cancelling && await db.LandedCostSources.AnyAsync(s => s.InvoiceId == invoice.Id
                                                                     && db.LandedCosts.Any(c => c.Id == s.LandedCostId && c.Status == LandedCostStatus.Posted), ct))
            throw PurchasingErrors.Conflict("invoice_in_landed_cost", "Fatura işlenmiş bir masraf dağıtımının kaynağı; önce masraf dağıtımını iptal edin.");
        return Applies(invoice) ? await correction.KeysAsync(invoice, cancelling, ct) : [];
    }

    public Task OnIssuedAsync(Invoice invoice, CancellationToken ct) {
        return Applies(invoice) ? correction.ApplyAsync(invoice, ct) : Task.CompletedTask;
    }

    public Task OnCancelledAsync(Invoice invoice, CancellationToken ct) {
        return Applies(invoice) ? correction.ReverseAsync(invoice, ct) : Task.CompletedTask;
    }
}
