namespace ERP.Domain.Platform;

/// <summary>
///     Bir outbox olayının, süreç içi bir aboneye (IOutboxSubscriber) iletim durumu: deneme sayısı, son hata, sıradaki deneme zamanı ve tamamlanma.
///     Olay akışa iletildiğinde (OutboxRelay) abone başına bir satır açılır; tamamlanana ya da deneme sınırına varana kadar yeniden denenir.
///     Aboneler iş işleminin dışında çalıştığından, bir aboneyin hatası iş değişikliğini geri almaz.
/// </summary>
public class OutboxSubscriberState : ITenantOwned {
    public Guid MessageId { get; set; }

    public string Subscriber { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Deneme sınırı aşıldı: artık denenmez (hata günlüğe yazılmıştır).</summary>
    public bool GaveUp { get; set; }

    public string? LastError { get; set; }

    public Guid TenantId { get; set; }
}
