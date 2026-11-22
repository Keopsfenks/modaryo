using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Sessions;

public class SessionGrantTests : TestBase
{
    private static async Task<Modaryo.Sessions.SessionRecord> RecordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return (await store.ListForUserAsync(userId, CancellationToken.None)).Single().Record;
    }

    private static async Task<Guid[]> AddBranchesAsync(Guid tenantId, int count)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var branches = Enumerable.Range(1, count)
            .Select(i => new Branch { TenantId = tenantId, Code = $"T{i}", Name = $"Şube {i}", CreatedAt = DateTimeOffset.UtcNow })
            .ToArray();
        db.Branches.AddRange(branches);
        await db.SaveChangesAsync(CancellationToken.None);
        return branches.Select(b => b.Id).ToArray();
    }

    private static async Task<Guid> AddRoleMemberAsync(Guid tenantId, Guid userId, bool allBranches, Guid[] roleBranches, Guid[] narrowing, decimal discount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var role = new TenantRole { TenantId = tenantId, Name = "Şube rolü " + Guid.NewGuid(), AllBranches = allBranches, MaxDiscountPercent = discount };
        role.Branches.AddRange(roleBranches.Select(b => new RoleBranch { BranchId = b }));
        db.TenantRoles.Add(role);
        var membership = new Membership { TenantId = tenantId, UserId = userId, RoleId = role.Id, JoinedAt = DateTimeOffset.UtcNow };
        membership.Branches.AddRange(narrowing.Select(b => new MembershipBranch { BranchId = b }));
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(CancellationToken.None);
        return role.Id;
    }

    private static (Guid, Guid, Guid) AsTuple(Guid[] ids) => (ids[0], ids[1], ids[2]);

    [Test]
    public async Task Login_SessionContainsModulesBranchesDiscountAndUserType()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        (await owner.PutAsJsonAsync("/api/identity/companies/current/modules", new { enabled = new[] { "pos" } })).EnsureSuccessStatusCode();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var memberClient = member;

        var record = await RecordAsync(memberId);

        record.Modules.ShouldBe(["pos"]);
        record.BranchIds.ShouldBeNull();
        record.MaxDiscountPct.ShouldBe(0m);
        record.UserType.ShouldBe("employee");
        record.CariId.ShouldBeNull();
    }

    [Test]
    public async Task Login_BranchScope_IsRoleScopeIntersectedWithMembershipNarrowing()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var user = await Factory.CreateConfirmedAsync("uye@ornek.com");
        var (a, b, c) = AsTuple(await AddBranchesAsync(tenantId, 3));
        await AddRoleMemberAsync(tenantId, user.Id, false, [a, b], [b, c], 7.5m);

        using var client = Factory.CreateClientWithCookies();
        (await client.LoginAsync("uye@ornek.com")).EnsureSuccessStatusCode();

        var record = await RecordAsync(user.Id);
        record.BranchIds.ShouldBe([b]);
        record.MaxDiscountPct.ShouldBe(7.5m);
        var session = await client.SessionAsync();
        session.GetProperty("branchIds").EnumerateArray().Select(x => x.GetGuid()).ShouldBe([b]);
        session.GetProperty("maxDiscountPercent").GetDecimal().ShouldBe(7.5m);
    }

    [Test]
    public async Task Login_EmptyBranchIntersection_IsEmptyArrayNotNull()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var user = await Factory.CreateConfirmedAsync("uye@ornek.com");
        var ids = await AddBranchesAsync(tenantId, 2);
        await AddRoleMemberAsync(tenantId, user.Id, false, [ids[0]], [ids[1]], 0m);

        using var client = Factory.CreateClientWithCookies();
        (await client.LoginAsync("uye@ornek.com")).EnsureSuccessStatusCode();

        (await RecordAsync(user.Id)).BranchIds.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Test]
    public async Task Login_AllBranchesRole_WithNarrowing_UsesNarrowing()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var user = await Factory.CreateConfirmedAsync("uye@ornek.com");
        var a = (await AddBranchesAsync(tenantId, 1))[0];
        await AddRoleMemberAsync(tenantId, user.Id, true, [], [a], 0m);

        using var client = Factory.CreateClientWithCookies();
        (await client.LoginAsync("uye@ornek.com")).EnsureSuccessStatusCode();

        (await RecordAsync(user.Id)).BranchIds.ShouldBe([a]);
    }

    [Test]
    public async Task GetSession_ExposesModulesAndBranches()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        await owner.CreateCompanyAsync();
        (await owner.PutAsJsonAsync("/api/identity/companies/current/modules", new { enabled = new[] { "crm" } })).EnsureSuccessStatusCode();

        var session = await owner.SessionAsync();

        session.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldBe(["crm"]);
        session.GetProperty("branchIds").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        session.GetProperty("maxDiscountPercent").GetDecimal().ShouldBe(0m);
        session.GetProperty("userType").GetString().ShouldBe("employee");
    }
}
