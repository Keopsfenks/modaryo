using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Modules;

public class ModuleTests : TestBase
{
    private const string Url = "/api/identity/companies/current/modules";

    private static async Task<Modaryo.Sessions.SessionRecord> RecordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        return (await store.ListForUserAsync(userId, CancellationToken.None)).Single().Record;
    }

    [Test]
    public async Task GetModules_ListsCatalogWithEnabledFlag()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        (await owner.PutAsJsonAsync(Url, new { enabled = new[] { "pos" } })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var memberClient = member;

        using var response = await member.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await response.JsonAsync()).EnumerateArray().ToArray();
        items.Length.ShouldBe(ModuleKeys.All.Count);
        items.Single(i => i.GetProperty("key").GetString() == "pos").GetProperty("enabled").GetBoolean().ShouldBeTrue();
        items.Single(i => i.GetProperty("key").GetString() == "crm").GetProperty("enabled").GetBoolean().ShouldBeFalse();
        items[0].GetProperty("wave").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task SetModules_UnknownKey_Returns400UnknownModule()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        await owner.CreateCompanyAsync();

        using var response = await owner.PutAsJsonAsync(Url, new { enabled = new[] { "pos", "yok-boyle-modul" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("unknown_module");
    }

    [Test]
    public async Task SetModules_RequiresSettingsModules_Returns403ForMember()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var memberClient = member;

        using var response = await member.PutAsJsonAsync(Url, new { enabled = new[] { "pos" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SetModules_UpdatesLiveSessionsOfAllMembers()
    {
        using var owner = await Factory.SignedInClientAsync("sahip@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, memberId) = await Factory.SignedInMemberAsync(tenantId, "uye@ornek.com", "member");
        using var memberClient = member;
        var ownerId = (await owner.SessionAsync()).GetProperty("user").GetProperty("id").GetGuid();

        using var response = await owner.PutAsJsonAsync(Url, new { enabled = new[] { "pos", "pos", "crm" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RecordAsync(ownerId)).Modules.OrderBy(m => m).ShouldBe(["crm", "pos"]);
        (await RecordAsync(memberId)).Modules.ShouldContain("pos");
        (await member.SessionAsync()).GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldContain("pos");
    }
}
