namespace IdentityService.Domain.Entities;

/// <summary>Firma bazlı rol. Hazır roller <see cref="SystemKey"/> taşır (owner/admin/member).</summary>
public class TenantRole : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SystemKey { get; set; }

    public bool IsSystem { get; set; }

    public List<TenantRolePermission> Permissions { get; } = [];
}
