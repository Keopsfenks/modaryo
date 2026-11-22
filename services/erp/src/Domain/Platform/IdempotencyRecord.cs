namespace ERP.Domain.Platform;

/// <summary>
///     İç API yazma isteklerinin idempotency kaydı. Anahtar (Caller, Key). Status 0: istek hâlâ işleniyor
///     (ayrılmış kayıt); aksi halde kayıtlı yanıt. Firma filtresi yoktur: çağıran servis firmaları başlıkla seçer.
///     7 günden eski kayıtlar silinir.
/// </summary>
public class IdempotencyRecord {
    public string Key { get; set; } = string.Empty;

    public string Caller { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public string RequestHash { get; set; } = string.Empty;

    /// <summary>0 = işleniyor, aksi halde yanıtın HTTP durumu.</summary>
    public int Status { get; set; }

    public string ResponseJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
