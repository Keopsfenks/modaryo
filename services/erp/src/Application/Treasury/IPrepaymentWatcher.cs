namespace ERP.Application.Treasury;

/// <summary>Siparişe bağlı tahsilat kaydedilince satış tarafını uyarır. Çağıranın açık işleminde, cari kilidi alınmış olarak çalışır.</summary>
public interface IPrepaymentWatcher {
    Task OnAdvanceRecordedAsync(Guid salesOrderId, CancellationToken ct);
}
