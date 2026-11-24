using ERP.Application.Inventory.Ledger;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory;

/// <summary>
///     Stok defterinin tek yazıcısı. Çağıran açık bir işlem (BeginTransactionAsync) içinde çağırır ve işlemi kendisi onaylar;
///     servis satırları ekler ve tutarlılık için işlem içinde SaveChanges çağırır, ancak işlemi asla onaylamaz.
///     Her yazım dönem kilidini ve şube yetkisini denetler; depo x kalem başına işlem düzeyinde danışma kilidi alır.
/// </summary>
public interface IStockService {
    Task<IReadOnlyList<Guid>> ReceiveAsync(MovementType type, string sourceType, Guid sourceId, DateOnly date,
                                           IReadOnlyList<StockLine> lines, CancellationToken ct);

    /// <summary>Set ürün satırlarını bileşenlerine açar (BundleIssue).</summary>
    Task<IReadOnlyList<Guid>> IssueAsync(MovementType type, string sourceType, Guid sourceId, DateOnly date,
                                         IReadOnlyList<StockLine> lines, CancellationToken ct);

    /// <summary>
    ///     Depolar arası transfer: her satır için kaynakta TransferOut, hedefte TransferIn yazılır. Hedef giriş, kaynak çıkışın
    ///     birim maliyetini taşır; geriye dönük yeniden değerlemede ikisi birlikte yeniden maliyetlenir. Çıkış hareketlerinin kimliklerini döndürür.
    /// </summary>
    Task<IReadOnlyList<Guid>> TransferAsync(string sourceType, Guid sourceId, DateOnly date, IReadOnlyList<TransferLine> lines,
                                            CancellationToken ct);

    /// <summary>Kaynağın hareketlerinin tersini yazar (iptal); hiçbir şey silmez. İkinci çağrı yeni hareket yazmaz.</summary>
    Task ReverseAsync(string sourceType, Guid sourceId, DateOnly date, CancellationToken ct);

    Task ReserveAsync(string sourceType, Guid sourceId, IReadOnlyList<ReserveLine> lines, CancellationToken ct);

    /// <summary>sourceLineId null ise kaynağın tüm aktif rezervasyonlarını bırakır.</summary>
    Task ReleaseAsync(string sourceType, Guid sourceId, Guid? sourceLineId, CancellationToken ct);

    /// <summary>
    ///     Rezervasyonu tüketir ve SalesShipment çıkış hareketini yazar. Rezerve edilenden fazlası da çıkılır.
    ///     movementSource* verilmezse hareket rezervasyonun kaynağına bağlanır (sevkiyat iptali için sevkiyatı verin).
    /// </summary>
    Task ConsumeAsync(string sourceType, Guid sourceId, Guid sourceLineId, decimal baseQuantity, CancellationToken ct,
                      string? movementSourceType = null, Guid? movementSourceId = null, DateOnly? date = null,
                      bool exemptFromStockCheck = false);

    /// <summary>Sevkiyat iptali: tüketilmiş rezervasyonu (en fazla baseQuantity kadar) tekrar Active yapar.</summary>
    Task RestoreConsumedAsync(string sourceType, Guid sourceId, Guid sourceLineId, decimal baseQuantity, CancellationToken ct);

    /// <summary>
    ///     Siparişe göre tedarik girişi: beklenen girişe bağlı rezervasyonlar (en eskiden) alıcı depoya taşınır.
    ///     productId verilirse yalnızca o kalemin (variantId ile birlikte) rezervasyonları eşleşir.
    /// </summary>
    Task RetargetExpectedAsync(Guid expectedReceiptSourceId, Guid warehouseId, decimal baseQuantity, CancellationToken ct,
                               Guid? productId = null, Guid? variantId = null);

    /// <summary>Giriş hareketinin toplam maliyetini deltaTotalTl kadar düzeltir (Revaluation hareketi, miktar 0).</summary>
    Task CorrectCostAsync(Guid warehouseId, Guid productId, Guid? variantId, DateOnly date, Guid inboundMovementId,
                          decimal deltaTotalTl, string sourceType, Guid sourceId, CancellationToken ct);
}
