namespace IdentityService.Domain.Entities;

public class TenantRolePermission
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;
}
