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
    string?            ResponseJson = null
);

/// <summary>Idempotency kayıtlarının deposu; servis uygular (ERP: PostgreSQL). Anahtar: (Caller, Key).</summary>
public interface IInternalIdempotencyStore {
    /// <summary>Önce bekleyen kaydı ekler (yarışa karşı güvenli); varsa mevcut kaydın durumunu döndürür.</summary>
    Task<IdempotencyReservation> ReserveAsync(string            caller,
                                              string            key,
                                              Guid              tenantId,
                                              string            requestHash,
                                              CancellationToken ct);

    Task CompleteAsync(string caller, string key, int status, string responseJson, CancellationToken ct);

    /// <summary>Ayrılan kaydı siler (5xx veya istisna sonrası aynı anahtarla yeniden deneme yapılabilsin).</summary>
    Task ReleaseAsync(string caller, string key, CancellationToken ct);
}
