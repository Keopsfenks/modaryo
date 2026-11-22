namespace IdentityService.Domain.Entities;

/// <summary>Denetim kaydı. Before / After JSON metnidir.</summary>
public class AuditEntry : BaseEntity
{
    public Guid TenantId { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ActorLabel { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? Before { get; set; }

    public string? After { get; set; }

    public DateTimeOffset At { get; set; }
}
