using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Roles;

public class RoleTests : TestBase
{
    private const string Url = "/api/identity/companies/current/roles";

    private static async Task<Modaryo.Sessions.SessionRecord> RecordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return (await store.ListForUserAsync(userId, CancellationToken.None)).Single().Record;
    }

    /// <summary>Hazır rolün izinlerini doğrudan veritabanında verilen kümeyle değiştirir.</summary>
    private static async Task SetRolePermissionsAsync(Guid roleId, params string[] permissions)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var role = await db.TenantRoles.Include(r => r.Permissions).SingleAsync(r => r.Id == roleId);
        role.Permissions.Clear();
        await db.SaveChangesAsync(CancellationToken.None);
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = roleId, Permission = p }));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> AddBranchAsync(Guid tenantId, string code)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var branch = new Branch { TenantId = tenantId, Code = code, Name = "Şube " + code, CreatedAt = DateTimeOffset.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(CancellationToken.None);
        return branch.Id;
    }

    private static string[] Perms(System.Text.Json.JsonElement role) =>
        role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();

    private static async Task<(HttpClient Owner, Guid TenantId)> OwnerWithCompanyAsync()
    {
        var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        return (owner, tenantId);
    }

    private static async Task<System.Text.Json.JsonElement> RoleBySystemKeyAsync(HttpClient client, string key) =>
        (await (await client.GetAsync(Url)).JsonAsync()).EnumerateArray().Single(r => r.GetProperty("systemKey").GetString() == key);

    [Test]
    public async Task CreateRole_WithPermissions_Returns201AndAppearsInList()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var response = await owner.PostAsJsonAsync(Url, new
        {
            name = "Muhasebe",
            permissions = new[] { "cari.read", "cari.manage" },
            maxDiscountPercent = 12.5m,
            allBranches = true,
            branchIds = Array.Empty<Guid>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        created.GetProperty("name").GetString().ShouldBe("Muhasebe");
        created.GetProperty("locked").GetBoolean().ShouldBeFalse();
        created.GetProperty("memberCount").GetInt32().ShouldBe(0);
        Perms(created).ShouldBe(["cari.manage", "cari.read"]);

        var list = (await (await owner.GetAsync(Url)).JsonAsync()).EnumerateArray().ToArray();
        list.Select(r => r.GetProperty("name").GetString()).ShouldBe(["Sahip", "Yönetici", "Kullanıcı", "Muhasebe"]);
        var owners = list[0];
        owners.GetProperty("locked").GetBoolean().ShouldBeTrue();
        Perms(owners).ShouldBe(["*"]);
        list[3].GetProperty("maxDiscountPercent").GetDecimal().ShouldBe(12.5m);
    }

    [Test]
    public async Task CreateRole_CopyFromAdmin_CopiesPermissionsAndDiscount()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var adminId = await Factory.RoleIdAsync(tenantId, "admin");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            (await db.TenantRoles.SingleAsync(r => r.Id == adminId)).MaxDiscountPercent = 20m;
            await db.SaveChangesAsync(CancellationToken.None);
        }

        using var response = await owner.PostAsJsonAsync(Url, new { name = "Yönetici kopyası", copyFromRoleId = adminId });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        Perms(created).ShouldBe(Perms(await RoleBySystemKeyAsync(owner, "admin")));
        created.GetProperty("maxDiscountPercent").GetDecimal().ShouldBe(20m);
        created.GetProperty("systemKey").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task CreateRole_GrantingPermissionCallerLacks_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var adminRoleId = await Factory.RoleIdAsync(tenantId, "admin");
        await SetRolePermissionsAsync(adminRoleId, IdentityService.Domain.Constants.Permissions.RolesManage, IdentityService.Domain.Constants.Permissions.RolesRead, IdentityService.Domain.Constants.Permissions.CariRead);
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        using var _a = admin;

        using var response = await admin.PostAsJsonAsync(Url, new { name = "Hileli", permissions = new[] { "cari.manage" } });
        using var allowed = await admin.PostAsJsonAsync(Url, new { name = "Okuyucu", permissions = new[] { "cari.read" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task UpdateRole_CallerCannotAddPermissionTheyLack_ButMayKeepExisting()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Muhasebe", permissions = new[] { "cari.read", "cari.manage" } });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var adminRoleId = await Factory.RoleIdAsync(tenantId, "admin");
        await SetRolePermissionsAsync(adminRoleId, IdentityService.Domain.Constants.Permissions.RolesManage, IdentityService.Domain.Constants.Permissions.RolesRead, IdentityService.Domain.Constants.Permissions.CariRead);
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        using var _a = admin;

        using var escalate = await admin.PutAsJsonAsync($"{Url}/{roleId}", new { name = "Muhasebe", permissions = new[] { "cari.read", "cari.manage", "cari.movements" } });
        using var keep = await admin.PutAsJsonAsync($"{Url}/{roleId}", new { name = "Muhasebe 2", permissions = new[] { "cari.read", "cari.manage" } });

        escalate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escalate.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        keep.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateRole_UnknownPermission_Returns400()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var response = await owner.PostAsJsonAsync(Url, new { name = "Garip", permissions = new[] { "yok.boyle" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateRole_StarPermission_Returns400()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var response = await owner.PostAsJsonAsync(Url, new { name = "Herşey", permissions = new[] { "*" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateRole_InvalidNameOrDiscount_Returns400()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        (await owner.PostAsJsonAsync(Url, new { name = "A", permissions = Array.Empty<string>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Url, new { name = new string('x', 51), permissions = Array.Empty<string>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Url, new { name = "Tamam", maxDiscountPercent = 100.5m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Url, new { name = "Tamam", maxDiscountPercent = 10.123m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Url, new { name = "Tamam", maxDiscountPercent = -1m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestCase("Muhasebe", "MUHASEBE")]
    [TestCase("ılık", "ILIK")]
    public async Task CreateRole_DuplicateNameDifferentCase_Returns409RoleNameTaken(string first, string second)
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        (await owner.PostAsJsonAsync(Url, new { name = first, permissions = Array.Empty<string>() })).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await owner.PostAsJsonAsync(Url, new { name = second, permissions = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("role_name_taken");
    }

    [Test]
    public async Task UpdateOwnerRole_Returns409RoleLocked()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var ownerRoleId = await Factory.RoleIdAsync(tenantId, "owner");

        using var response = await owner.PutAsJsonAsync($"{Url}/{ownerRoleId}", new { name = "Başka", permissions = new[] { "cari.read" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("role_locked");
    }

    [Test]
    public async Task DeleteRole_WithMembers_Returns409RoleInUse()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Kasiyer", permissions = new[] { "cari.read" } });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        (await owner.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await owner.DeleteAsync($"{Url}/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("role_in_use");
        (await (await owner.GetAsync($"{Url}/{roleId}")).JsonAsync()).GetProperty("memberCount").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task DeleteRole_Unused_Returns204()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Geçici", permissions = Array.Empty<string>() });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        using var response = await owner.DeleteAsync($"{Url}/{roleId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Url}/{roleId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteRole_SystemRole_Returns409RoleLocked()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var adminId = await Factory.RoleIdAsync(tenantId, "admin");

        using var response = await owner.DeleteAsync($"{Url}/{adminId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("role_locked");
    }

    [Test]
    public async Task ResetAdminRole_RestoresDefaultPermissions()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var adminId = await Factory.RoleIdAsync(tenantId, "admin");
        (await owner.PutAsJsonAsync($"{Url}/{adminId}", new { name = "Yönetici", permissions = new[] { "cari.read" } })).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await owner.PostAsync($"{Url}/{adminId}/reset", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Perms(await response.JsonAsync()).ShouldBe(SystemRoles.DefaultPermissions[SystemRoles.Admin].Order(StringComparer.Ordinal).ToArray());
        var ownerRoleId = await Factory.RoleIdAsync(tenantId, "owner");
        var ownerReset = await owner.PostAsync($"{Url}/{ownerRoleId}/reset", null);
        ownerReset.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ownerReset.ErrorCodeAsync()).ShouldBe("role_locked");
    }

    [Test]
    public async Task UpdateRole_UpdatesLiveSessionPermissionsAndDiscount()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Kasiyer", permissions = new[] { "cari.read" }, maxDiscountPercent = 5m });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        (await owner.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).MaxDiscountPct.ShouldBe(5m);

        using var response = await owner.PutAsJsonAsync($"{Url}/{roleId}", new
        {
            name = "Kasiyer",
            permissions = new[] { "cari.read", "cari.movements" },
            maxDiscountPercent = 15m,
            allBranches = true,
            branchIds = Array.Empty<Guid>(),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var record = await RecordAsync(memberId);
        record.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray().ShouldBe(["cari.movements", "cari.read"]);
        record.MaxDiscountPct.ShouldBe(15m);
    }

    [Test]
    public async Task UpdateRole_BranchScopeAppliesToLiveSession()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var branchId = await AddBranchAsync(tenantId, "S1");
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Şube rolü", permissions = new[] { "cari.read" } });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        (await owner.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await owner.PutAsJsonAsync($"{Url}/{roleId}", new { name = "Şube rolü", allBranches = false, branchIds = new[] { branchId } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.JsonAsync();
        dto.GetProperty("allBranches").GetBoolean().ShouldBeFalse();
        dto.GetProperty("branchIds").EnumerateArray().Select(b => b.GetGuid()).ShouldBe([branchId]);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([branchId]);
    }

    [Test]
    public async Task UpdateRole_BranchesOfOtherTenant_Returns400()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var other = await Factory.SignedInClientAsync("baska@ornek.com");
        var otherTenant = await other.CreateCompanyAsync("Diğer A.Ş.");
        var foreignBranch = await AddBranchAsync(otherTenant, "X1");
        using var created = await owner.PostAsJsonAsync(Url, new { name = "Şube rolü", permissions = new[] { "cari.read" } });
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        using var response = await owner.PutAsJsonAsync($"{Url}/{roleId}", new { name = "Şube rolü", allBranches = false, branchIds = new[] { foreignBranch } });
        using var empty = await owner.PutAsJsonAsync($"{Url}/{roleId}", new { name = "Şube rolü", allBranches = false, branchIds = Array.Empty<Guid>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Member_WithoutRolesManage_Returns403OnCreate()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;

        using var response = await member.PostAsJsonAsync(Url, new { name = "Yeni", permissions = Array.Empty<string>() });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetRoles_WithoutAnyReadPermission_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var memberRoleId = await Factory.RoleIdAsync(tenantId, "member");
        await SetRolePermissionsAsync(memberRoleId, IdentityService.Domain.Constants.Permissions.CariRead);
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;

        (await member.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
