using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Companies;

public class CompanyTests : TestBase
{
    private static string[] Permissions(JsonElement session) =>
        session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();

    [Test]
    public async Task CreateCompany_MakesCallerOwnerAndSwitchesSession()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        var tenantId = await client.CreateCompanyAsync("Akdeniz Tekstil");

        var session = await client.SessionAsync();
        session.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantId);
        session.GetProperty("activeCompany").GetProperty("name").GetString().ShouldBe("Akdeniz Tekstil");
        session.GetProperty("activeCompany").GetProperty("roleName").GetString().ShouldBe("Sahip");
        Permissions(session).ShouldBe(["*"]);
    }

    [Test]
    public async Task CreateCompany_InvalidName_Returns400()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var response = await client.PostAsJsonAsync("/api/identity/companies", new { name = " A " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateCompany_WithoutSession_Returns401()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync("/api/identity/companies", new { name = "Test" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateCompany_WhenAlreadyInCompany_AddsSecondMembership()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");
        await client.CreateCompanyAsync("Birinci");

        var second = await client.CreateCompanyAsync("İkinci");

        var session = await client.SessionAsync();
        session.GetProperty("companies").GetArrayLength().ShouldBe(2);
        session.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(second);
    }

    [Test]
    public async Task SwitchTenant_BetweenOwnCompanies_ChangesActiveAndIsRememberedAtNextLogin()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");
        var first = await client.CreateCompanyAsync("Birinci");
        await client.CreateCompanyAsync("İkinci");

        using var switched = await client.PostAsJsonAsync("/api/identity/auth/session/tenant", new { tenantId = first });

        switched.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.SessionAsync()).GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(first);
        using var otherDevice = Factory.CreateClientWithCookies();
        var login = await (await otherDevice.LoginAsync("ali@ornek.com")).JsonAsync();
        login.GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(first);
    }

    [Test]
    public async Task GetCurrentCompany_WithoutActiveCompany_Returns404()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");

        using var response = await client.GetAsync("/api/identity/companies/current");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UpdateCompany_AdminAllowed_MemberForbidden()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync("Eski Ad");
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");

        (await member.PatchAsJsonAsync("/api/identity/companies/current", new { name = "Yeni Ad" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PatchAsJsonAsync("/api/identity/companies/current", new { name = "Yeni Ad" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var company = await (await owner.GetAsync("/api/identity/companies/current")).JsonAsync();
        company.GetProperty("name").GetString().ShouldBe("Yeni Ad");
        admin.Dispose();
        member.Dispose();
    }

    [Test]
    public async Task Roles_And_Members_AreListed()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com", "Sahip Kişi");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");

        var roles = await (await member.GetAsync("/api/identity/companies/current/roles")).JsonAsync();
        var members = await (await member.GetAsync("/api/identity/companies/current/members")).JsonAsync();

        roles.EnumerateArray().Select(r => r.GetProperty("systemKey").GetString()).ShouldBe(["owner", "admin", "member"]);
        members.GetArrayLength().ShouldBe(2);
        var row = members.EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == memberId);
        row.GetProperty("email").GetString().ShouldBe("uye@ornek.com");
        row.GetProperty("roleName").GetString().ShouldBe("Kullanıcı");
        member.Dispose();
    }

    [Test]
    public async Task LastOwner_CannotDemoteLeaveOrBeRemoved()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var ownerId = (await owner.SessionAsync()).GetProperty("user").GetProperty("id").GetGuid();
        var adminRole = await Factory.RoleIdAsync(tenantId, "admin");

        using var demote = await owner.PatchAsJsonAsync($"/api/identity/companies/current/members/{ownerId}", new { roleId = adminRole });
        using var leave = await owner.PostAsync("/api/identity/companies/current/leave", null);
        using var remove = await owner.DeleteAsync($"/api/identity/companies/current/members/{ownerId}");

        demote.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await demote.ErrorCodeAsync()).ShouldBe("last_owner");
        leave.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await leave.ErrorCodeAsync()).ShouldBe("last_owner");
        remove.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await remove.ErrorCodeAsync()).ShouldBe("last_owner");
    }

    [Test]
    public async Task ChangeRole_UpdatesTargetsLiveSessionPermissions()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        var adminRole = await Factory.RoleIdAsync(tenantId, "admin");

        using var response = await owner.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId = adminRole });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Permissions(await member.SessionAsync()).ShouldBe(["ayarlar.moduller", "ayarlar.subeler", "cari.manage", "cari.movements", "cari.read", "company.update", "denetim.read", "members.invite", "members.manage", "members.read", "roller.manage", "roller.read"]);
        (await member.PatchAsJsonAsync("/api/identity/companies/current", new { name = "Artık yapabilir" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        member.Dispose();
    }

    [Test]
    public async Task RemoveMember_TargetSessionLosesTenantAndClockStarts()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");

        using var response = await owner.DeleteAsync($"/api/identity/companies/current/members/{memberId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var session = await member.SessionAsync();
        session.GetProperty("activeCompany").ValueKind.ShouldBe(JsonValueKind.Null);
        Permissions(session).ShouldBeEmpty();
        (await member.GetAsync("/api/identity/companies/current/members")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserAccounts>().FindByIdAsync(memberId, CancellationToken.None);
        user!.LastMembershipEndedAt.ShouldNotBeNull();
        member.Dispose();
    }

    [Test]
    public async Task Leave_NonOwner_LosesTenant()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");

        using var response = await member.PostAsync("/api/identity/companies/current/leave", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.SessionAsync()).GetProperty("companies").GetArrayLength().ShouldBe(0);
        member.Dispose();
    }

    [Test]
    public async Task SecondOwner_AllowsFirstOwnerToLeave()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (coOwner, _) = await Factory.SignedInMemberAsync(tenantId, "ortak@ornek.com", "owner");

        using var response = await owner.PostAsync("/api/identity/companies/current/leave", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        coOwner.Dispose();
    }

    [Test]
    public async Task ChangeRole_ForMemberOfOtherTenant_Returns404()
    {
        using var ownerA = await Factory.SignedInClientAsync("a@ornek.com");
        var tenantA = await ownerA.CreateCompanyAsync("Firma A");
        using var ownerB = await Factory.SignedInClientAsync("b@ornek.com");
        var tenantB = await ownerB.CreateCompanyAsync("Firma B");
        var (memberB, memberBId) = await Factory.SignedInMemberAsync(tenantB, "uye-b@ornek.com", "member");
        var adminRoleA = await Factory.RoleIdAsync(tenantA, "admin");
        var adminRoleB = await Factory.RoleIdAsync(tenantB, "admin");

        using var withOwnRole = await ownerA.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberBId}", new { roleId = adminRoleA });
        using var withOtherRole = await ownerA.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberBId}", new { roleId = adminRoleB });
        using var remove = await ownerA.DeleteAsync($"/api/identity/companies/current/members/{memberBId}");

        withOwnRole.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        withOtherRole.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        remove.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await memberB.SessionAsync()).GetProperty("activeCompany").GetProperty("id").GetGuid().ShouldBe(tenantB);
        memberB.Dispose();
    }

    [Test]
    public async Task ChangeRole_ToRoleOfOtherTenant_Returns404()
    {
        using var ownerA = await Factory.SignedInClientAsync("a@ornek.com");
        var tenantA = await ownerA.CreateCompanyAsync("Firma A");
        var (memberA, memberAId) = await Factory.SignedInMemberAsync(tenantA, "uye-a@ornek.com", "member");
        using var ownerB = await Factory.SignedInClientAsync("b@ornek.com");
        var tenantB = await ownerB.CreateCompanyAsync("Firma B");
        var ownerRoleB = await Factory.RoleIdAsync(tenantB, "owner");

        using var response = await ownerA.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberAId}", new { roleId = ownerRoleB });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        memberA.Dispose();
    }

    [Test]
    public async Task Admin_CannotGrantOrRemoveOwner_Returns403()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var ownerId = (await owner.SessionAsync()).GetProperty("user").GetProperty("id").GetGuid();
        var (admin, _) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "admin");
        var (_, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        var ownerRole = await Factory.RoleIdAsync(tenantId, "owner");

        (await admin.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId = ownerRole })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/identity/companies/current/members/{ownerId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        admin.Dispose();
    }

    [Test]
    public async Task Member_CannotChangeRoles_Returns403()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        var adminRole = await Factory.RoleIdAsync(tenantId, "admin");

        using var response = await member.PatchAsJsonAsync($"/api/identity/companies/current/members/{memberId}", new { roleId = adminRole });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        member.Dispose();
    }

    [Test]
    public async Task CreateCompany_CreatesDefaultMerkezBranch()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");
        var tenantId = await client.CreateCompanyAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var branches = await db.Branches.Where(b => b.TenantId == tenantId).ToListAsync();

        var branch = branches.ShouldHaveSingleItem();
        branch.IsDefault.ShouldBeTrue();
        branch.IsActive.ShouldBeTrue();
        branch.Code.ShouldBe("MRK");
        branch.Name.ShouldBe("Merkez");
    }

    [Test]
    public async Task CreateCompany_StartsWithNoModulesAndLatestPermissionSet()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");
        var tenantId = await client.CreateCompanyAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);

        tenant.EnabledModules.ShouldBeEmpty();
        tenant.AppliedPermissionSetVersion.ShouldBe(SystemRoles.LatestPermissionSetVersion);
        var roles = await db.TenantRoles.Where(r => r.TenantId == tenantId).ToListAsync();
        roles.ShouldAllBe(r => r.AllBranches && r.MaxDiscountPercent == 0m);
        (await db.Memberships.SingleAsync(m => m.TenantId == tenantId)).Kind.ShouldBe("employee");
    }
}
