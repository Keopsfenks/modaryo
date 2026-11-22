namespace IdentityService.Domain.Entities;

/// <summary>Kullanıcı ↔ firma ↔ rol üyeliği. (TenantId, UserId) benzersizdir.</summary>
public class Membership : BaseEntity
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public TenantRole Role { get; set; } = null!;

    public DateTimeOffset JoinedAt { get; set; }
}
