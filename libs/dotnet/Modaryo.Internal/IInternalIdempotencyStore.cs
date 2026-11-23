namespace Modaryo.Internal;

public enum IdempotencyOutcome {
    /// <summary>Anahtar bu istek için ayrıldı; istek çalıştırılmalı.</summary>
    Reserved,

    /// <summary>Anahtar farklı bir istekle kullanılmış.</summary>
    Conflict,

    /// <summary>Aynı istek hâlâ işleniyor.</summary>
    InProgress,

    /// <summary>Aynı istek daha önce tamamlanmış; kayıtlı yanıt döndürülmeli.</summary>
    Replay
}

public sealed record IdempotencyReservation(
    IdempotencyOutcome Outcome,
    int                Status       = 0,
    string?            ResponseJson = null,
    Guid               OwnerToken   = default
);

/// <summary>Idempotency kayıtlarının deposu; servis uygular (ERP: PostgreSQL). Anahtar: (Caller, Key).</summary>
public interface IInternalIdempotencyStore {
    /// <summary>
    ///     Önce bekleyen kaydı ekler (yarışa karşı güvenli); varsa mevcut kaydın durumunu döndürür.
    ///     <paramref name="pendingTimeout" /> süresinden eski, aynı özetli bekleyen kayıt silinip yeniden ayrılır (en çok bir kez).
    /// </summary>
    Task<IdempotencyReservation> ReserveAsync(string            caller,
                                              string            key,
                                              Guid              tenantId,
                                              string            requestHash,
                                              TimeSpan          pendingTimeout,
                                              CancellationToken ct);

    /// <summary>
    ///     Kaydı tamamlar. <paramref name="ownerToken" />, ayırma sonucundaki sahiplik belirtecidir; kayıt başkasına
    ///     devredilmişse (zaman aşımı sonrası devralma) hiçbir şey yapılmaz.
    /// </summary>
    Task CompleteAsync(string            caller,
                       string            key,
                       Guid              ownerToken,
                       int               status,
                       string            responseJson,
                       CancellationToken ct);

    /// <summary>
    ///     Ayrılan kaydı siler (5xx veya istisna sonrası aynı anahtarla yeniden deneme yapılabilsin); yalnızca
    ///     <paramref name="ownerToken" /> hâlâ kaydın sahibiyse siler.
    /// </summary>
    Task ReleaseAsync(string caller, string key, Guid ownerToken, CancellationToken ct);
}
