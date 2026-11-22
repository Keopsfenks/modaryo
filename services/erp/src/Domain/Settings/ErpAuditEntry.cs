namespace ERP.Domain.Settings;

/// <summary>Firma içindeki önemli değişikliklerin silinmeyen kaydı (kim, ne zaman, neyi, önce/sonra).</summary>
public class ErpAuditEntry : BaseEntity, ITenantOwned {
    public Guid TenantId { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ActorLabel { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>Önceki değer (JSON); yoksa null.</summary>
    public string? Before { get; set; }

    /// <summary>Sonraki değer (JSON); yoksa null.</summary>
    public string? After { get; set; }
}
