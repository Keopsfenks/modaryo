namespace ERP.Application.Purchasing;

/// <summary>
///     Satın alma tarafının satış siparişine bildirimleri. Uygulamalar çağıranın açık işleminde çalışır; işlem başlatmaz ya da onaylamaz
///     ve yalnızca satış satırı düzeyinde yazar (kilit sırası: satın alma siparişi, stok; cari ve sipariş başlığı kilidi alınmaz).
/// </summary>
public interface IPurchaseLinkListener {
    /// <summary>Siparişe göre tedarik satırına mal kabul geldi.</summary>
    Task OnReceivedAsync(Guid poLineId, decimal qty, string? deliveryNoteNo, CancellationToken ct);

    /// <summary>Satın alma siparişi kısa kapatıldı ya da iptal edildi.</summary>
    Task OnPoClosedAsync(Guid poId, CancellationToken ct);
}
