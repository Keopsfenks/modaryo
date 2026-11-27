using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Treasury;
using Microsoft.Extensions.Logging;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

/// <summary>
///     Siparişe bağlı tahsilat kaydedilince ön ödeme beklemedeki siparişi yeniden değerlendirir. Tahsilat işleminin içinde, cari kilidi alınmış olarak çalışır;
///     yetki aranmaz (tahsilatı yapan kullanıcının satış onay yetkisi olmayabilir). Stok yetersizse sipariş AwaitingPayment kalır ve tahsilat yine de kaydedilir.
/// </summary>
public sealed class PrepaymentWatcher(
    IApplicationDbContext db,
    ApprovalPipeline      pipeline,
    ILogger<PrepaymentWatcher> logger
) : IPrepaymentWatcher {
    public async Task OnAdvanceRecordedAsync(Guid salesOrderId, CancellationToken ct) {
        var waiting = await db.SalesOrders.AsNoTracking().AnyAsync(o => o.Id == salesOrderId && o.Status == SalesOrderStatus.AwaitingPayment, ct);
        if (!waiting) return;

        // Tahsilat her durumda kaydedilir: işlem hattının iş kuralı hataları (tedarikçi eksik, şube yetkisi vb.) tahsilatı geri almaz. İşlem hattı ilk yazımdan
        // önce tüm kontrolleri bitirir; hata durumunda sipariş AwaitingPayment kalır ve elle onay (POST approve) aynı hattı yeniden dener (idempotent).
        try {
            await pipeline.RunAsync(salesOrderId, ApprovalEntry.Watcher, ct);
        } catch (Exception e) when (e is AppException or ForbiddenAccessException) {
            // Tahsilat zaten kaydedildi (SaveChanges poster tarafında yapıldı); hattın yarım bıraktığı izlenen değişiklikler atılır.
            db.ClearTracking();
            logger.LogWarning(e, "Ön ödeme sonrası sipariş {OrderId} serbest bırakılamadı; sipariş AwaitingPayment kaldı.", salesOrderId);
        }
    }
}
