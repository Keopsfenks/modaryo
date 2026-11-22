using System.Text.Json;

namespace IdentityService.Web.FunctionalTests.Audit;

public class AuditTests : TestBase
{
    private const string AuditUrl = "/api/identity/companies/current/audit";
    private const string RolesUrl = "/api/identity/companies/current/roles";
    private const string ModulesUrl = "/api/identity/companies/current/modules";

    private static async Task<JsonElement> AuditAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync(AuditUrl + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static string[] Perms(JsonElement snapshot) =>
        snapshot.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();

    [Test]
    public async Task UpdateRole_WritesAuditWithBeforeAndAfterPermissions()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        await owner.CreateCompanyAsync();
        using var created = await owner.PostAsJsonAsync(RolesUrl, new { name = "Satış", permissions = new[] { "cari.read" } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var roleId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        using var updated = await owner.PutAsJsonAsync($"{RolesUrl}/{roleId}", new { name = "Satış", permissions = new[] { "cari.read", "cari.manage" } });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await AuditAsync(owner, "?entityType=role");
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        page.GetProperty("total").GetInt32().ShouldBe(2);
        items[0].GetProperty("action").GetString().ShouldBe("role.updated");
        items[0].GetProperty("entityId").GetString().ShouldBe(roleId.ToString());
        items[0].GetProperty("actorLabel").GetString().ShouldNotBeNullOrWhiteSpace();
        Perms(items[0].GetProperty("before")).ShouldBe(["cari.read"]);
        Perms(items[0].GetProperty("after")).OrderBy(p => p).ShouldBe(["cari.manage", "cari.read"]);
        items[1].GetProperty("action").GetString().ShouldBe("role.created");
        items[1].GetProperty("before").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task SetModules_WritesAudit()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        await owner.CreateCompanyAsync();

        (await owner.PutAsJsonAsync(ModulesUrl, new { enabled = new[] { "pos" } })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = (await AuditAsync(owner, "?entityType=modules")).GetProperty("items").EnumerateArray().ToArray();
        items.Length.ShouldBe(1);
        items[0].GetProperty("action").GetString().ShouldBe("modules.updated");
        items[0].GetProperty("after").GetProperty("enabled").EnumerateArray().Select(e => e.GetString()).ShouldBe(["pos"]);
    }

    [Test]
    public async Task Audit_OtherTenantInvisible()
    {
        using var ownerA = await Factory.SignedInClientAsync("a@ornek.com");
        await ownerA.CreateCompanyAsync("A Ltd.");
        (await ownerA.PutAsJsonAsync(ModulesUrl, new { enabled = new[] { "pos" } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ownerB = await Factory.SignedInClientAsync("b@ornek.com");
        await ownerB.CreateCompanyAsync("B Ltd.");

        (await AuditAsync(ownerA)).GetProperty("total").GetInt32().ShouldBe(1);
        (await AuditAsync(ownerB)).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task Audit_RequiresAuditRead()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var memberClient = member;

        using var response = await member.GetAsync(AuditUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task BranchAndMemberChanges_WriteAudit_AndFailedChangeWritesNone()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        using var branch = await owner.PostAsJsonAsync("/api/identity/companies/current/branches", new { code = "ist", name = "İstanbul" });
        branch.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var duplicate = await owner.PostAsJsonAsync("/api/identity/companies/current/branches", new { code = "IST", name = "Yinelenen" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        member.Dispose();

        using var removed = await owner.DeleteAsync($"/api/identity/companies/current/members/{memberId}");
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var branches = (await AuditAsync(owner, "?entityType=branch")).GetProperty("items").EnumerateArray().ToArray();
        branches.Length.ShouldBe(1);
        branches[0].GetProperty("action").GetString().ShouldBe("branch.created");
        branches[0].GetProperty("after").GetProperty("code").GetString().ShouldBe("IST");
        var members = (await AuditAsync(owner, "?entityType=member")).GetProperty("items").EnumerateArray().ToArray();
        members.Length.ShouldBe(1);
        members[0].GetProperty("action").GetString().ShouldBe("member.removed");
        members[0].GetProperty("entityId").GetString().ShouldBe(memberId.ToString());
    }

    [Test]
    public async Task Audit_HugePage_Returns200Empty()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        await owner.CreateCompanyAsync();

        var page = await AuditAsync(owner, "?page=2147483647");

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}
