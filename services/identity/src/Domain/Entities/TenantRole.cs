namespace IdentityService.Domain.Entities;

/// <summary>Firma bazlı rol. Hazır roller <see cref="SystemKey"/> taşır (owner/admin/member).</summary>
public class TenantRole : BaseEntity
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SystemKey { get; set; }

    public bool IsSystem { get; set; }

    /// <summary>İskonto sınırı (yüzde, 0-100).</summary>
    public decimal MaxDiscountPercent { get; set; }

    /// <summary>true ise tüm şubelere erişir; değilse yalnız Branches.</summary>
    public bool AllBranches { get; set; } = true;

    public List<TenantRolePermission> Permissions { get; } = [];

    public List<RoleBranch> Branches { get; } = [];
}
