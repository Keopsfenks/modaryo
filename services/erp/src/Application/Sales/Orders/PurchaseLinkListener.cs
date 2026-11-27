using ERP.Application.Common.Interfaces;
using ERP.Application.Purchasing;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Sales.Orders;

/// <summary>
///     Satın alma bildirimlerini satış satırlarına yansıtır. Çağıranın işleminde çalışır; yalnızca satır düzeyinde yazar
///     (cari ve sipariş başlığı kilidi alınmaz: satın alma tarafı sipariş kilidi + stok kilidi sırasıyla çalışır).
///     Zaman çizgisi okunurken satın alma verisinden türetilir; burada saklanmaz.
/// </summary>
public sealed class PurchaseLinkListener(
    IApplicationDbContext db,
    IOutbox               outbox
) : IPurchaseLinkListener {
    public async Task OnReceivedAsync(Guid poLineId, decimal qty, string? deliveryNoteNo, CancellationToken ct) {
        var line = await db.SalesOrderLines.AsNoTracking().Where(l => l.PurchaseOrderLineId == poLineId).Select(l => new { l.Id, l.SalesOrderId }).FirstOrDefaultAsync(ct);
        if (line is null) return;

        outbox.Enqueue("sales.supply-received", new { salesOrderId = line.SalesOrderId, salesOrderLineId = line.Id, poLineId, quantity = qty, deliveryNoteNo });
    }

    public async Task OnPoClosedAsync(Guid poId, CancellationToken ct) {
        var lines = await db.SalesOrderLines.Where(l => l.PurchaseOrderId == poId).ToListAsync(ct);
        if (lines.Count == 0) return;

        var po        = await db.PurchaseOrders.FindAsync([poId], ct);
        var cancelled = po?.Status == PurchaseOrderStatus.Cancelled;
        var lineIds   = lines.Select(l => l.Id).ToList();

        // Beklenen girişe bağlı kalmış rezervasyonlar (satın alma tarafı genellikle zaten bıraktı; tekrar bırakmak zararsızdır).
        var leftovers = await db.Reservations.Where(r => r.SourceType == SalesStock.SourceType && lineIds.Contains(r.SourceLineId) && r.ExpectedReceiptSourceId == poId
                                                         && r.Status == ReservationStatus.Active).ToListAsync(ct);
        foreach (var r in leftovers) r.Status = ReservationStatus.Released;

        if (cancelled)
            foreach (var l in lines) {
                l.PurchaseOrderId     = null;
                l.PurchaseOrderLineId = null;
            }

        outbox.Enqueue(cancelled ? "sales.supply-cancelled" : "sales.supply-closed",
                       new { poId, salesOrderIds = lines.Select(l => l.SalesOrderId).Distinct().ToList(), salesOrderLineIds = lineIds });
        await db.SaveChangesAsync(ct);
    }
}
