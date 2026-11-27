using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Web.FunctionalTests.Permissions;

public class PermissionCatalogTests : TestBase
{
    private const string Url = "/api/identity/permissions";

    private static IEnumerable<JsonElement> Entries(JsonElement body) =>
        body.GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("permissions").EnumerateArray());

    [Test]
    public async Task Catalog_AsOwner_ReturnsGroupedPermissions()
    {
        using var client = await Factory.SignedInClientAsync("ali@ornek.com");
        await client.CreateCompanyAsync();

        using var response = await client.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        var groups = body.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("group").GetString()).ShouldBe(
            ["Firma", "Üyeler", "Roller", "Ayarlar", "Cari hesaplar", "Katalog", "Fiyatlandırma", "Stok", "Fatura", "Finans", "Satın alma", "Satış", "Sevkiyat", "Denetim"]);
        groups.ShouldAllBe(g => g.GetProperty("moduleKey").ValueKind == JsonValueKind.Null);
        var codes = Entries(body).Select(e => e.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("roller.read");
        codes.ShouldContain("cari.movements");
        codes.ShouldNotContain("*");
        Entries(body).ShouldAllBe(e => e.GetProperty("name").GetString() != "" && e.GetProperty("description").GetString() != "");
    }

    [Test]
    public async Task Catalog_RequiresRolesRead_Returns403ForMember()
    {
        using var owner = await Factory.SignedInClientAsync("ali@ornek.com");
        var tenantId = await owner.CreateCompanyAsync();
        var (member, _) = await Factory.SignedInMemberAsync(tenantId, "veli@ornek.com", SystemRoles.Member);
        using var _ = member;

        using var response = await member.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Catalog_WithoutSession_Returns401()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.GetAsync(Url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Catalog_HidesPermissionsOfDisabledModules()
    {
        const string code = "pos.test-satis";
        PermissionCatalog.RegisterForTests(new PermissionInfo(code, "pos", "Perakende Satış", "Test satışı", "Yalnızca test içindir."));
        try
        {
            using var client = await Factory.SignedInClientAsync("ali@ornek.com");
            var tenantId = await client.CreateCompanyAsync();

            using (var before = await client.GetAsync(Url))
            {
                Entries(await before.JsonAsync()).Select(e => e.GetProperty("code").GetString()).ShouldNotContain(code);
            }

            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
                tenant.EnabledModules = ["pos"];
                await db.SaveChangesAsync(CancellationToken.None);
            }

            using var after = await client.GetAsync(Url);
            var body = await after.JsonAsync();
            Entries(body).Select(e => e.GetProperty("code").GetString()).ShouldContain(code);
            var group = body.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("group").GetString() == "Perakende Satış");
            group.GetProperty("moduleKey").GetString().ShouldBe("pos");
        }
        finally
        {
            PermissionCatalog.ResetForTests();
        }
    }
}
