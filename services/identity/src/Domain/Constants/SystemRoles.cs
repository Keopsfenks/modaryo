using IdentityService.Domain.Entities;

namespace IdentityService.Domain.Constants;

/// <summary>Her firma oluşturulduğunda otomatik eklenen hazır roller.</summary>
public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    /// <summary>Güncel hazır rol izin seti sürümü (Görev 6 bunu hesaplanan özellikle değiştirir).</summary>
    public const int LatestPermissionSetVersion = 1;

    /// <summary>Hazır rollerin izinleri (tek kaynak). Yeni modül izinleri buraya eklenir; açılışta mevcut firmalara dağıtılır.</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultPermissions { get; } = new Dictionary<string, string[]>
    {
        [Owner] = [Permissions.All],
        [Admin] =
        [
            Permissions.CompanyUpdate, Permissions.MembersRead, Permissions.MembersInvite, Permissions.MembersManage,
            Permissions.CariRead, Permissions.CariManage, Permissions.CariMovements,
        ],
        [Member] = [Permissions.MembersRead, Permissions.CariRead],
    };

    public static IReadOnlyList<TenantRole> CreateDefaults(Guid tenantId) =>
    [
        Create(tenantId, "Sahip", Owner, DefaultPermissions[Owner]),
        Create(tenantId, "Yönetici", Admin, DefaultPermissions[Admin]),
        Create(tenantId, "Kullanıcı", Member, DefaultPermissions[Member]),
    ];

    private static TenantRole Create(Guid tenantId, string name, string systemKey, string[] permissions)
    {
        var role = new TenantRole { TenantId = tenantId, Name = name, SystemKey = systemKey, IsSystem = true };
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        return role;
    }
}
