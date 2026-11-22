namespace ERP.Domain.Settings;

/// <summary>
///     Firmanın bir ayar kategorisi (firma bilgileri, genel, mali). Değer JSON olarak tutulur; Version hem
///     iyimser eşzamanlılık denetimi hem de EF eşzamanlılık belirteci olarak kullanılır. Anahtar: (TenantId, Category).
/// </summary>
public class TenantSetting : ITenantOwned {
    public Guid TenantId { get; set; }

    public string Category { get; set; } = string.Empty;

    /// <summary>jsonb sütunu.</summary>
    public string Json { get; set; } = "{}";

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;
}
