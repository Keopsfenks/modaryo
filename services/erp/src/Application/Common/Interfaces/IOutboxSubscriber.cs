namespace ERP.Application.Common.Interfaces;

/// <summary>Aboneye iletilen outbox olayı (zarf alanları; veri JSON metnidir).</summary>
public sealed record OutboxEvent(Guid Id, string Type, string PayloadJson, Guid TenantId, DateTimeOffset OccurredAt);

/// <summary>
///     Süreç içi outbox abonesi. OutboxRelay olayı akışa ilettikten sonra, olayın firması etkin bir DI kapsamında çağırır; iş işleminden
///     bağımsızdır, bu yüzden bir hata iş değişikliğini geri almaz: abone başına deneme durumu (OutboxSubscriberState) tutulur ve
///     hata geri çekilmeyle yeniden denenir, sınır aşılınca günlüğe yazılıp bırakılır. At-least-once: abone tekrarlara dayanıklı olmalıdır.
/// </summary>
public interface IOutboxSubscriber {
    /// <summary>Gerçekten bir iş yapıldıysa true (deneme sayılır); yapılacak bir şey yoksa false (ör. alıcı adresi yok).</summary>
    Task<bool> HandleAsync(OutboxEvent message, CancellationToken ct);
}
