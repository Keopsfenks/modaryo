using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Roles;

public class PermissionSyncTests : TestBase
{
    private static async Task<int> SyncAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SystemRolePermissionSync>().RunAsync(CancellationToken.None);
    }

    private static async Task<(Guid TenantId, Guid CustomRoleId)> SeedLegacyTenantAsync(int version = 1)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant { Name = "Eski Firma", CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = Guid.CreateVersion7(), AppliedPermissionSetVersion = version };
        db.Tenants.Add(tenant);
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Sahip", "owner", ["*"]));
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Yönetici", "admin",
            AdminV1));
        db.TenantRoles.Add(LegacyRole(tenant.Id, "Kullanıcı", "member", ["members.read", "cari.read"]));
        var custom = LegacyRole(tenant.Id, "Muhasebe", null, ["members.read"]);
        db.TenantRoles.Add(custom);
        await db.SaveChangesAsync();
        return (tenant.Id, custom.Id);
    }

    private static readonly string[] AdminV1 =
        ["company.update", "members.read", "members.invite", "members.manage", "cari.read", "cari.manage", "cari.movements"];

    private static readonly string[] NewCodes =
        ["ayarlar.moduller", "ayarlar.subeler", "denetim.read", "roller.manage", "roller.read"];

    private static TenantRole LegacyRole(Guid tenantId, string name, string? systemKey, string[] permissions)
    {
        var role = new TenantRole { TenantId = tenantId, Name = name, SystemKey = systemKey, IsSystem = systemKey is not null };
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        return role;
    }

    private static async Task<string[]> PermissionsOfAsync(Guid tenantId, string? systemKey, string? name = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await db.TenantRoles.Include(r => r.Permissions)
            .SingleAsync(r => r.TenantId == tenantId && r.SystemKey == systemKey && (name == null || r.Name == name));
        return role.Permissions.Select(p => p.Permission).Order().ToArray();
    }

    [Test]
    public async Task Sync_AppliesVersion2ToExistingTenant()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        var added = await SyncAsync();

        added.ShouldBeGreaterThanOrEqualTo(5);
        var admin = await PermissionsOfAsync(tenantId, "admin");
        admin.ShouldBe(AdminV1.Concat(NewCodes).Order().ToArray());
        (await PermissionsOfAsync(tenantId, "member")).ShouldBe(["cari.read", "members.read"]);
        (await PermissionsOfAsync(tenantId, "owner")).ShouldBe(["*"]);
    }

    [Test]
    public async Task Sync_DoesNotReaddPermissionRemovedByUser()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(SystemRoles.LatestPermissionSetVersion);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await db.TenantRoles.Include(r => r.Permissions)
                .SingleAsync(r => r.TenantId == tenantId && r.SystemKey == "admin");
            role.Permissions.RemoveAll(p => p.Permission == "cari.movements");
            await db.SaveChangesAsync();
        }

        await SyncAsync();

        (await PermissionsOfAsync(tenantId, "admin")).ShouldNotContain("cari.movements");
    }

    [Test]
    public async Task Sync_ConcurrentRuns_DoNotFail()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        var results = await Task.WhenAll(SyncAsync(), SyncAsync());

        results.Min().ShouldBe(0);
        (await PermissionsOfAsync(tenantId, "admin")).Length.ShouldBe(AdminV1.Length + NewCodes.Length);
    }

    [Test]
    public async Task Sync_IsIdempotent()
    {
        await SeedLegacyTenantAsync(1);

        (await SyncAsync()).ShouldBeGreaterThan(0);
        (await SyncAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Sync_DoesNotTouchCustomRoles()
    {
        var (tenantId, _) = await SeedLegacyTenantAsync(1);

        await SyncAsync();

        (await PermissionsOfAsync(tenantId, null, "Muhasebe")).ShouldBe(["members.read"]);
    }
}
