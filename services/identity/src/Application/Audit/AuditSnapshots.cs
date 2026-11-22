using IdentityService.Domain.Entities;

namespace IdentityService.Application.Audit;

public static class AuditActions
{
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string RoleReset = "role.reset";
    public const string ModulesUpdated = "modules.updated";
    public const string BranchCreated = "branch.created";
    public const string BranchUpdated = "branch.updated";
    public const string BranchDeleted = "branch.deleted";
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberBranchesChanged = "member.branches_changed";
    public const string MemberRemoved = "member.removed";
}

public static class AuditEntities
{
    public const string Role = "role";
    public const string Modules = "modules";
    public const string Branch = "branch";
    public const string Member = "member";
}

/// <summary>Denetim kaydına yazılan küçük anlık görüntüler. Koleksiyonlar kopyalanır; sonradan değişse de görüntü sabit kalır.</summary>
public static class AuditSnapshots
{
    public static object Role(TenantRole role) => new
    {
        name = role.Name,
        permissions = role.Permissions.Select(p => p.Permission).Order(StringComparer.Ordinal).ToArray(),
        maxDiscountPercent = role.MaxDiscountPercent,
        allBranches = role.AllBranches,
        branchIds = role.Branches.Select(b => b.BranchId).Order().ToArray(),
    };

    public static object Branch(Branch branch) => new { code = branch.Code, name = branch.Name, isActive = branch.IsActive };

    public static object Modules(IEnumerable<string> enabled) => new { enabled = enabled.ToArray() };

    public static object MemberRole(Guid roleId, string roleName) => new { roleId, roleName };

    public static object MemberBranches(Membership member) => new
    {
        restricted = member.BranchesRestricted,
        branchIds = member.Branches.Select(b => b.BranchId).Order().ToArray(),
    };
}
