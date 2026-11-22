using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Branches;

public partial class BranchTests : TestBase
{
    private const string Url = "/api/identity/companies/current/branches";
    private const string RolesUrl = "/api/identity/companies/current/roles";
    private const string MembersUrl = "/api/identity/companies/current/members";

    private static async Task<Modaryo.Sessions.SessionRecord> RecordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return (await store.ListForUserAsync(userId, CancellationToken.None)).Single().Record;
    }

    private static async Task<(HttpClient Owner, Guid TenantId)> OwnerWithCompanyAsync()
    {
        var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        return (owner, await owner.CreateCompanyAsync());
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient client, string code, string name = "Şube")
    {
        using var response = await client.PostAsJsonAsync(Url, new { code, name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> DefaultBranchIdAsync(HttpClient client) =>
        (await (await client.GetAsync(Url)).JsonAsync()).EnumerateArray().Single(b => b.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();

    private static async Task GrantPermissionsAsync(Guid tenantId, string systemKey, params string[] permissions)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var role = await db.TenantRoles.Include(r => r.Permissions).SingleAsync(r => r.TenantId == tenantId && r.SystemKey == systemKey);
        role.Permissions.Clear();
        await db.SaveChangesAsync(CancellationToken.None);
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Üyeye verilen şube kapsamlı yeni bir rol oluşturup atar; oturumu hemen güncellenir.</summary>
    private static async Task<Guid> BranchRoleForAsync(HttpClient owner, Guid memberId, string name, string[] permissions, bool allBranches, params Guid[] branchIds)
    {
        using var created = await owner.PostAsJsonAsync(RolesUrl, new { name, permissions, allBranches, branchIds });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();
        (await owner.PatchAsJsonAsync($"{MembersUrl}/{memberId}", new { roleId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return roleId;
    }

    private static string?[] Codes(System.Text.Json.JsonElement list) =>
        list.EnumerateArray().Select(b => b.GetProperty("code").GetString()).ToArray();

    [Test]
    public async Task CreateBranch_Returns201()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var response = await owner.PostAsJsonAsync(Url, new { code = "sb1", name = "İstanbul" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.JsonAsync();
        dto.GetProperty("code").GetString().ShouldBe("SB1");
        dto.GetProperty("name").GetString().ShouldBe("İstanbul");
        dto.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        dto.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        Codes(await (await owner.GetAsync(Url)).JsonAsync()).ShouldBe(["MRK", "SB1"]);
    }

    [Test]
    public async Task CreateBranch_InvalidLengths_Returns400()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;

        using var longCode = await owner.PostAsJsonAsync(Url, new { code = "ABCDEFGHIJK", name = "Şube" });
        using var emptyCode = await owner.PostAsJsonAsync(Url, new { code = " ", name = "Şube" });
        using var shortName = await owner.PostAsJsonAsync(Url, new { code = "A1", name = "A" });

        longCode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        emptyCode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        shortName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task DuplicateCode_Returns409()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var id = await CreateBranchAsync(owner, "S1");
        var other = await CreateBranchAsync(owner, "S2");

        using var dup = await owner.PostAsJsonAsync(Url, new { code = "s1", name = "Yinelenen" });
        using var merkez = await owner.PostAsJsonAsync(Url, new { code = "mrk", name = "Yinelenen" });
        using var rename = await owner.PutAsJsonAsync($"{Url}/{other}", new { code = "S1", name = "Şube", isActive = true });
        using var sameSelf = await owner.PutAsJsonAsync($"{Url}/{id}", new { code = "s1", name = "Yeni ad", isActive = true });

        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await dup.ErrorCodeAsync()).ShouldBe("branch_code_taken");
        merkez.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        rename.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await rename.ErrorCodeAsync()).ShouldBe("branch_code_taken");
        sameSelf.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task UpdateBranch_ChangesFieldsAndDeactivates()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var id = await CreateBranchAsync(owner, "S1");

        using var response = await owner.PutAsJsonAsync($"{Url}/{id}", new { code = "S9", name = "Yeni ad", isActive = false });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.JsonAsync();
        dto.GetProperty("code").GetString().ShouldBe("S9");
        dto.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        Codes(await (await owner.GetAsync(Url)).JsonAsync()).ShouldBe(["MRK", "S9"]);
    }

    [Test]
    public async Task DeleteDefault_Returns409BranchIsDefault()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var defaultId = await DefaultBranchIdAsync(owner);

        using var response = await owner.DeleteAsync($"{Url}/{defaultId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("branch_is_default");
    }

    [Test]
    public async Task DeactivateDefault_Returns409()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var defaultId = await DefaultBranchIdAsync(owner);

        using var response = await owner.PutAsJsonAsync($"{Url}/{defaultId}", new { code = "MRK", name = "Merkez", isActive = false });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("branch_is_default");
    }

    [Test]
    public async Task DeleteBranch_RemovesScopesAndRefreshesSessions()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        await BranchRoleForAsync(owner, memberId, "Şubeli", ["cari.read"], false, b1, b2);
        (await RecordAsync(memberId)).BranchIds!.Order().ShouldBe(new[] { b1, b2 }.Order());

        using var response = await owner.DeleteAsync($"{Url}/{b1}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([b2]);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        (await db.Branches.AnyAsync(b => b.Id == b1)).ShouldBeFalse();
    }

    [Test]
    public async Task RoleWithSelectedBranches_SessionHasThoseBranchIds()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;

        await BranchRoleForAsync(owner, memberId, "Şubeli", ["cari.read"], false, b1);

        (await RecordAsync(memberId)).BranchIds.ShouldBe([b1]);
        (await member.SessionAsync()).GetProperty("branchIds").EnumerateArray().Select(b => b.GetGuid()).ShouldBe([b1]);
    }

    [Test]
    public async Task MemberNarrowing_IntersectsRoleScope_UpdatesLiveSession()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var b3 = await CreateBranchAsync(owner, "S3");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        await BranchRoleForAsync(owner, memberId, "Şubeli", ["cari.read"], false, b1, b2);

        using var narrow = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { b2, b3 } });

        narrow.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([b2]);
        var listed = (await (await owner.GetAsync(MembersUrl)).JsonAsync()).EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == memberId);
        listed.GetProperty("branchIds").EnumerateArray().Select(b => b.GetGuid()).Order().ShouldBe(new[] { b2, b3 }.Order());

        using var disjoint = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { b3 } });
        disjoint.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).BranchIds.ShouldBe([]);

        using var clear = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = (Guid[]?)null });
        clear.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RecordAsync(memberId)).BranchIds!.Order().ShouldBe(new[] { b1, b2 }.Order());
        var cleared = (await (await owner.GetAsync(MembersUrl)).JsonAsync()).EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == memberId);
        cleared.GetProperty("branchIds").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Test]
    public async Task MemberNarrowing_OtherTenantBranch_Returns400()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        using var other = await Factory.SignedInClientAsync("baska@ornek.com");
        await other.CreateCompanyAsync("Diğer A.Ş.");
        var foreign = await CreateBranchAsync(other, "X1");
        var inactive = await CreateBranchAsync(owner, "S1");
        (await owner.PutAsJsonAsync($"{Url}/{inactive}", new { code = "S1", name = "Şube", isActive = false })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;

        using var response = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { foreign } });
        using var passive = await owner.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { inactive } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        passive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task MemberNarrowing_BranchOutsideCallerScope_Returns403()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        var b1 = await CreateBranchAsync(owner, "S1");
        var b2 = await CreateBranchAsync(owner, "S2");
        var (caller, callerId) = await Factory.SignedInMemberAsync(tenantId, "yonetici@ornek.com", "member");
        using var _c = caller;
        await BranchRoleForAsync(owner, callerId, "Sınırlı", ["members.manage", "members.read"], false, b1);
        var (target, targetId) = await Factory.SignedInMemberAsync(tenantId, "hedef@ornek.com", "member");
        using var _t = target;

        using var outside = await caller.PutAsJsonAsync($"{MembersUrl}/{targetId}/branches", new { branchIds = new[] { b2 } });
        using var inside = await caller.PutAsJsonAsync($"{MembersUrl}/{targetId}/branches", new { branchIds = new[] { b1 } });

        outside.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outside.ErrorCodeAsync()).ShouldBe("permission_not_grantable");
        inside.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task ListBranches_OtherTenantInvisible()
    {
        var (owner, _) = await OwnerWithCompanyAsync();
        using var _o = owner;
        await CreateBranchAsync(owner, "S1");
        using var other = await Factory.SignedInClientAsync("baska@ornek.com");
        await other.CreateCompanyAsync("Diğer A.Ş.");
        var foreign = await CreateBranchAsync(other, "X1");

        var codes = Codes(await (await owner.GetAsync(Url)).JsonAsync());
        using var update = await owner.PutAsJsonAsync($"{Url}/{foreign}", new { code = "X1", name = "Şube", isActive = true });
        using var delete = await owner.DeleteAsync($"{Url}/{foreign}");

        codes.ShouldBe(["MRK", "S1"]);
        update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Member_WithoutBranchPermission_CanListButNotWrite()
    {
        var (owner, tenantId) = await OwnerWithCompanyAsync();
        using var _o = owner;
        await GrantPermissionsAsync(tenantId, "member", "cari.read");
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var _m = member;
        var defaultId = await DefaultBranchIdAsync(owner);

        (await member.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.PostAsJsonAsync(Url, new { code = "S1", name = "Şube" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"{Url}/{defaultId}", new { code = "MRK", name = "Merkez", isActive = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{Url}/{defaultId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"{MembersUrl}/{memberId}/branches", new { branchIds = new[] { defaultId } })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
