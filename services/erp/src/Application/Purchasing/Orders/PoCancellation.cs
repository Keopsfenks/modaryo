using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

/// <summary>
///     Satın alma siparişini iptal eden tek kod yolu (P6 iptali ve satış siparişi iptali aynı yolu kullanır).
///     GLOBAL KİLİT SIRASI (tüm yollar): cari -> satış siparişi -> satın alma siparişi -> stok anahtarları. Satın alma tarafı cari ve satış siparişi
///     kilidi almaz; sipariş kilidinden başlar, bu sıranın bir alt kümesidir. Satış tarafı satın alma siparişini yalnızca stok kilidinden ÖNCE kilitler.
///     Çağıran, satın alma siparişini <see cref="IApplicationDbContext.LockPurchaseOrderAsync" /> ile kilitlemiş ve durumunu kilit altında doğrulamış olmalıdır.
/// </summary>
public static class PoCancellation {
    public static async Task CancelLockedAsync(IApplicationDbContext db, IStockService stock, IOutbox outbox, IPurchaseLinkListener links, PurchaseOrder po, string reason,
                                               CancellationToken ct) {
        po.Status       = PurchaseOrderStatus.Cancelled;
        po.CancelReason = reason.Trim();

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
    }
}
