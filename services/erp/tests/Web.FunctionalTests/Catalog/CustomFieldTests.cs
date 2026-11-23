using System.Net.Http.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class CustomFieldTests : TestBase {
    private const           string Fields = "/api/erp/catalog/custom-fields";
    private static readonly Guid   Tenant = Guid.CreateVersion7();

    private static Task<HttpResponseMessage> Define(HttpClient client, string key, string type, bool required = false,
                                                    string[]?  options = null, bool service = true) {
        return client.PostAsJsonAsync(Fields, new {
            key, label = "Alan " + key, type, required, options, appliesToProduct = true, appliesToService = service
        });
    }

    private static object Product(string code, object? customFields, string kind = "product") {
        return new { kind, name = "Ürün " + code, code, customFields };
    }

    [Test]
    public async Task CustomFieldRequiredMissing_Returns400() {
        using var client = await Manager(Tenant);
        (await Define(client, "garanti", "number", true, service: false)).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var missing = await client.PostAsJsonAsync(Products, Product("F1", new { }));
        using var wrongType = await client.PostAsJsonAsync(Products, Product("F2", new { garanti = "iki yıl" }));

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        wrongType.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateOk(client, Product("F3", new { garanti = 24 }));
        ok.GetProperty("customFields").GetProperty("garanti").GetDecimal().ShouldBe(24m);

        // Zorunlu alan hizmete uygulanmadığı için hizmette istenmez.
        var service = await CreateOk(client, Product("F4", new { }, "service"));
        service.GetProperty("customFields").EnumerateObject().Count().ShouldBe(0);
    }

    [Test]
    public async Task CustomFieldSelectInvalidOption_Returns400() {
        using var client = await Manager(Tenant);
        (await Define(client, "renk_grubu", "select", options: ["sıcak", "soğuk"])).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var invalid = await client.PostAsJsonAsync(Products, Product("S1", new { renk_grubu = "ılık" }));

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ok = await CreateOk(client, Product("S2", new { renk_grubu = "sıcak" }));
        ok.GetProperty("customFields").GetProperty("renk_grubu").GetString().ShouldBe("sıcak");
    }

    [Test]
    public async Task Definitions_CrudAndValidation() {
        using var client = await Manager(Tenant);

        (await Define(client, "A", "text")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Define(client, "ok_key", "select")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Define(client, "uretici", "text")).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var duplicate = await Define(client, "uretici", "text");
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("key_taken");

        var list = await (await client.GetAsync(Fields)).JsonAsync();
        list.GetArrayLength().ShouldBe(1);
        var id = list[0].GetProperty("id").GetGuid();

        using var update = await client.PutAsJsonAsync($"{Fields}/{id}", new {
            label = "Üretici firma", required = true, appliesToProduct = true, appliesToService = false, sort = 2
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await update.JsonAsync()).GetProperty("label").GetString().ShouldBe("Üretici firma");

        // Tanım silinince kartlardaki eski değer sessizce atılır, kayıt engellenmez.
        (await client.DeleteAsync($"{Fields}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CreateOk(client, Product("D1", new { uretici = "X" })))
            .GetProperty("customFields").EnumerateObject().Count().ShouldBe(0);
    }

    [Test]
    public async Task Definitions_LimitOf30() {
        using var client = await Manager(Tenant);
        for (var i = 0; i < 30; i++)
            (await Define(client, $"alan_{i:00}", "text")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var over = await Define(client, "alan_31", "text");

        over.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Definitions_PermissionRules() {
        using var reader   = await Factory.ClientAsync(Tenant, "katalog.read", "katalog.manage");
        using var settings = await Factory.ClientAsync(Tenant, "ayarlar.katalog");
        using var nothing  = await Factory.ClientAsync(Tenant, "cari.read");

        (await reader.GetAsync(Fields)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await settings.GetAsync(Fields)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await nothing.GetAsync(Fields)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Define(reader, "x_alan", "text")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Define(settings, "x_alan", "bool")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
