namespace IdentityService.Domain.Entities;

/// <summary>
/// Firma daveti. Token'ın kendisi saklanmaz, yalnızca SHA-256 özeti (<see cref="TokenHash"/>).
/// </summary>
public class Invitation : BaseEntity
{
    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    /// <summary>Normalize edilmiş (küçük harf, kırpılmış) e-posta.</summary>
    public string Email { get; set; } = string.Empty;

    public Guid RoleId { get; set; }

    public TenantRole Role { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;
}
