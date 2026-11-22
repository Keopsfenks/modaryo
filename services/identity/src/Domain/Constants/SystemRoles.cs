using IdentityService.Domain.Entities;

namespace IdentityService.Domain.Constants;

/// <summary>Her firma oluşturulduğunda otomatik eklenen hazır roller.</summary>
public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    public static IReadOnlyList<TenantRole> CreateDefaults(Guid tenantId) =>
    [
        Create(tenantId, "Sahip", Owner, [Permissions.All]),
        Create(tenantId, "Yönetici", Admin,
            [Permissions.CompanyUpdate, Permissions.MembersRead, Permissions.MembersInvite, Permissions.MembersManage]),
        Create(tenantId, "Kullanıcı", Member, [Permissions.MembersRead]),
    ];

    private static TenantRole Create(Guid tenantId, string name, string systemKey, string[] permissions)
    {
        var role = new TenantRole { TenantId = tenantId, Name = name, SystemKey = systemKey, IsSystem = true };
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        return role;
    }
}
