using System.Net.Http.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

/// <summary>P1 son inceleme düzeltmeleri: seçenekler ucu (I1), boş kod (I5) ve zorunlu özel alanlar (I4).</summary>
public class FinalReviewTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private const string Options = "/api/erp/catalog/options";

    [Test]
    public async Task Options_ReadableWithKatalogReadOnly_ReturnsUnitsVatAndFields() {
        using var manager = await Manager(Tenant);
        using var current = await manager.GetAsync("/api/erp/settings/katalog");
        var       version = (await current.JsonAsync()).GetProperty("version").GetInt32();
        (await manager.PutAsJsonAsync("/api/erp/settings/katalog", new {
            value = new {
                codeTemplate = "U{00000}", defaultVatRate = 10, units = new[] { "adet", "ton" }, barcodePrefix = "2000000",
                nextBarcodeSequence = 1, nextCodeSequence = 1
            },
            version
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync("/api/erp/catalog/custom-fields", new {
            key = "renk_kodu", label = "Renk kodu", type = "text", required = false, appliesToProduct = true,
            appliesToService = true
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var read = await Factory.ClientAsync(Tenant, "katalog.read");
        (await read.GetAsync("/api/erp/settings/katalog")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var response = await read.GetAsync(Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        body.GetProperty("units").EnumerateArray().Select(u => u.GetString()).ShouldBe(["adet", "ton"]);
        body.GetProperty("defaultVatRate").GetInt32().ShouldBe(10);
        body.GetProperty("customFields")[0].GetProperty("key").GetString().ShouldBe("renk_kodu");
        // Şablon, önek ve sayaçlar yalnızca ayarlar.katalog iznine açıktır.
        body.TryGetProperty("codeTemplate", out _).ShouldBeFalse();
        body.TryGetProperty("nextCodeSequence", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Options_WithoutKatalogRead_Returns403() {
        using var none = await Factory.ClientAsync(Tenant, "cari.read");

        (await none.GetAsync(Options)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateWithoutCode_KeepsExistingCode() {
        using var client = await Manager(Tenant);
        var card = await CreateOk(client, new {
            kind = "product", code = "U00012", name = "Eski",
            attributes = new[] { new { name = "Renk", values = new[] { "Kırmızı" } } }
        });
        var id = card.GetProperty("id").GetGuid();

        using var response = await client.PutAsJsonAsync($"{Products}/{id}", new { kind = "product", name = "Yeni ad" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var updated = await response.JsonAsync();
        updated.GetProperty("code").GetString().ShouldBe("U00012");
        updated.GetProperty("name").GetString().ShouldBe("Yeni ad");
        updated.GetProperty("variants")[0].GetProperty("code").GetString().ShouldBe("U00012-1");
        (await (await client.GetAsync($"{Products}/next-code")).JsonAsync()).GetProperty("code").GetString().ShouldBe("U00013");
    }

    [Test]
    public async Task RequiredCustomField_OnlyEnforcedOnCreateOrWhenPresent() {
        using var client = await Manager(Tenant);
        var       old    = await CreateOk(client, new { kind = "product", code = "OLD1", name = "Eski kart" });
        (await client.PostAsJsonAsync("/api/erp/catalog/custom-fields", new {
            key = "garanti", label = "Garanti", type = "number", required = true, appliesToProduct = true,
            appliesToService = false
        })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = old.GetProperty("id").GetGuid();

        // Oluştururken zorunlu.
        (await client.PostAsJsonAsync(Products, new { kind = "product", code = "NEW1", name = "Yeni" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Güncellemede alan hiç gönderilmediyse zorunlu tutulmaz.
        (await client.PutAsJsonAsync($"{Products}/{id}", new { kind = "product", code = "OLD1", name = "Ad 2" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"{Products}/{id}", new { kind = "product", code = "OLD1", name = "Ad 3", customFields = new { } }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Alan gönderilip boşaltılırsa zorunludur.
        (await client.PutAsJsonAsync($"{Products}/{id}", new {
            kind = "product", code = "OLD1", name = "Ad 4", customFields = new { garanti = (int?)null }
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"{Products}/{id}", new {
            kind = "product", code = "OLD1", name = "Ad 5", customFields = new { garanti = 12 }
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
