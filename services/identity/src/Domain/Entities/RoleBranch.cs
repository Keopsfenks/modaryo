namespace IdentityService.Domain.Entities;

/// <summary>Rolün erişebildiği şube (rol AllBranches = false iken).</summary>
public class RoleBranch
{
    public Guid RoleId { get; set; }

    public Guid BranchId { get; set; }
}
