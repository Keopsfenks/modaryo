using ERP.Application.Common.Interfaces;
using ERP.Application.Treasury;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

/// <summary>
///     Siparişe bağlı tahsilat kaydedilince ön ödeme beklemedeki siparişi yeniden değerlendirir. Tahsilat işleminin içinde, cari kilidi alınmış olarak çalışır;
///     yetki aranmaz (tahsilatı yapan kullanıcının satış onay yetkisi olmayabilir). Stok yetersizse sipariş AwaitingPayment kalır ve tahsilat yine de kaydedilir.
/// </summary>
public sealed class PrepaymentWatcher(
    IApplicationDbContext db,
    ApprovalPipeline      pipeline
) : IPrepaymentWatcher {
    public async Task OnAdvanceRecordedAsync(Guid salesOrderId, CancellationToken ct) {
        var waiting = await db.SalesOrders.AsNoTracking().AnyAsync(o => o.Id == salesOrderId && o.Status == SalesOrderStatus.AwaitingPayment, ct);
        if (!waiting) return;

        await pipeline.RunAsync(salesOrderId, ApprovalEntry.Watcher, ct);
    }
}
