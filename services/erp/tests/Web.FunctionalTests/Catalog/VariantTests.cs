using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class VariantTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Product(string code, object? extra = null) {
        var body = new Dictionary<string, object?> { ["kind"] = "product", ["name"] = "Tişört", ["code"] = code };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        return body;
    }

    private static object Attributes(params (string Name, string[] Values)[] attributes) {
        return new { attributes = attributes.Select(a => new { name = a.Name, values = a.Values }).ToArray() };
    }

    private static async Task<JsonElement> SetAttributesOk(HttpClient client, Guid id, object body) {
        using var response = await client.PutAsJsonAsync($"{Products}/{id}/attributes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static JsonElement VariantByCode(JsonElement product, string code) {
        return product.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("code").GetString() == code);
    }

    [Test]
    public async Task SetAttributes_GeneratesCombinationsWithCodes() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VA1"));
        var id      = created.GetProperty("id").GetGuid();

        var result = await SetAttributesOk(client, id, Attributes(("Renk", ["Kırmızı", "Mavi"]), ("Beden", ["S", "M"])));

        result.GetProperty("attributes").GetArrayLength().ShouldBe(2);
        var variants = result.GetProperty("variants");
        variants.GetArrayLength().ShouldBe(4);
        variants.EnumerateArray().Select(v => v.GetProperty("code").GetString()).Order()
            .ShouldBe(["VA1-1", "VA1-2", "VA1-3", "VA1-4"]);
        var first = VariantByCode(result, "VA1-1").GetProperty("values");
        first.GetProperty("Renk").GetString().ShouldBe("Kırmızı");
        first.GetProperty("Beden").GetString().ShouldBe("S");

        // Hizmette varyant olamaz.
        var service = await CreateOk(client, new { kind = "service", name = "Montaj", code = "VAH1" });
        using var denied = await client.PutAsJsonAsync($"{Products}/{service.GetProperty("id").GetGuid()}/attributes",
                                                       Attributes(("Renk", ["Mavi"])));
        denied.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AddValue_KeepsExistingVariantIdsAndBarcodes() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VB1", Attributes(("Beden", ["S", "M"]))));
        var id      = created.GetProperty("id").GetGuid();
        var s       = VariantByCode(created, "VB1-1");
        var sId     = s.GetProperty("id").GetGuid();

        using var barcode = await client.PutAsJsonAsync($"{Products}/{id}/variants/{sId}",
                                                        new { code = "VB1-1", barcode = "8693001" });
        barcode.StatusCode.ShouldBe(HttpStatusCode.OK, await barcode.Content.ReadAsStringAsync());

        var result = await SetAttributesOk(client, id, Attributes(("Beden", ["S", "M", "L"])));

        result.GetProperty("variants").GetArrayLength().ShouldBe(3);
        var kept = VariantByCode(result, "VB1-1");
        kept.GetProperty("id").GetGuid().ShouldBe(sId);
        kept.GetProperty("barcode").GetString().ShouldBe("8693001");
        VariantByCode(result, "VB1-3").GetProperty("values").GetProperty("Beden").GetString().ShouldBe("L");
    }

    [Test]
    public async Task RemoveValueWithUsedVariant_Returns409() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VC1", Attributes(("Beden", ["S", "M"]))));
        var id      = created.GetProperty("id").GetGuid();
        Factory.ItemUsage.MarkVariantUsed(VariantByCode(created, "VC1-2").GetProperty("id").GetGuid());

        using var response = await client.PutAsJsonAsync($"{Products}/{id}/attributes", Attributes(("Beden", ["S"])));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("variant_in_use");

        // Hiçbir şey değişmedi.
        var product = await (await client.GetAsync($"{Products}/{id}")).JsonAsync();
        product.GetProperty("variants").GetArrayLength().ShouldBe(2);
        product.GetProperty("attributes")[0].GetProperty("values").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task RemoveValueUnused_DeletesVariant() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VD1", Attributes(("Beden", ["S", "M"]))));
        var id      = created.GetProperty("id").GetGuid();
        var m       = VariantByCode(created, "VD1-2").GetProperty("id").GetGuid();
        using var barcode = await client.PutAsJsonAsync($"{Products}/{id}/variants/{m}",
                                                         new { code = "VD1-2", barcode = "8694001" });
        barcode.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await SetAttributesOk(client, id, Attributes(("Beden", ["S"])));

        result.GetProperty("variants").GetArrayLength().ShouldBe(1);
        result.GetProperty("variants")[0].GetProperty("code").GetString().ShouldBe("VD1-1");

        // Silinen varyantın barkodu serbest kalır.
        var reuse = await CreateOk(client, Product("VD2", new { barcode = "8694001" }));
        reuse.GetProperty("barcode").GetString().ShouldBe("8694001");
    }

    [Test]
    public async Task Over500_Returns409() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VE1"));
        var id      = created.GetProperty("id").GetGuid();
        var values  = Enumerable.Range(1, 8).Select(n => "d" + n).ToArray();

        using var response = await client.PutAsJsonAsync($"{Products}/{id}/attributes",
                                                         Attributes(("A", values), ("B", values), ("C", values)));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("too_many_variants");
    }

    [Test]
    public async Task VariantBarcodeConflictsWithProductBarcode_Returns409() {
        using var client = await Manager(Tenant);
        await CreateOk(client, Product("VF1", new { barcode = "8695001" }));
        var created = await CreateOk(client, Product("VF2", new { barcode = "8695002" } ));
        var id      = created.GetProperty("id").GetGuid();
        var withVariants = await SetAttributesOk(client, id, Attributes(("Beden", ["S", "M"])));
        var variantId    = VariantByCode(withVariants, "VF2-1").GetProperty("id").GetGuid();

        // Başka kartın ürün barkoduyla çakışır.
        using var other = await client.PutAsJsonAsync($"{Products}/{id}/variants/{variantId}",
                                                      new { code = "VF2-1", barcode = "8695001" });
        // Aynı kartın kendi ürün barkoduyla da çakışır.
        using var own = await client.PutAsJsonAsync($"{Products}/{id}/variants/{variantId}",
                                                    new { code = "VF2-1", barcode = "8695002" });

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await other.ErrorCodeAsync()).ShouldBe("barcode_taken");
        own.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await own.ErrorCodeAsync()).ShouldBe("barcode_taken");
    }

    [Test]
    public async Task VariantPriceOverride_ReturnedInDto() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, Product("VG1", Attributes(("Beden", ["S", "M"]))));
        var id      = created.GetProperty("id").GetGuid();
        var variantId = VariantByCode(created, "VG1-1").GetProperty("id").GetGuid();

        using var response = await client.PutAsJsonAsync($"{Products}/{id}/variants/{variantId}", new {
            code = "VG1-S", salePriceOverride = new { amount = 150.5m, currency = "TRY", vatIncluded = true },
            purchasePriceOverride = new { amount = 90m, currency = "TRY", vatIncluded = false }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var product = await (await client.GetAsync($"{Products}/{id}")).JsonAsync();
        var variant = VariantByCode(product, "VG1-S");
        variant.GetProperty("salePriceOverride").GetProperty("amount").GetDecimal().ShouldBe(150.5m);
        variant.GetProperty("purchasePriceOverride").GetProperty("vatIncluded").GetBoolean().ShouldBeFalse();

        // Kod başka bir kartla çakışamaz.
        using var taken = await client.PutAsJsonAsync($"{Products}/{id}/variants/{variantId}", new { code = "VG1-2" });
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.ErrorCodeAsync()).ShouldBe("code_taken");
    }

    [Test]
    public async Task ActivateAndDeactivateVariant() {
        using var client = await Manager(Tenant);
        var created   = await CreateOk(client, Product("VH1", Attributes(("Beden", ["S"]))));
        var id        = created.GetProperty("id").GetGuid();
        var variantId = VariantByCode(created, "VH1-1").GetProperty("id").GetGuid();

        using var off = await client.PostAsync($"{Products}/{id}/variants/{variantId}/deactivate", null);
        off.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var product = await (await client.GetAsync($"{Products}/{id}")).JsonAsync();
        VariantByCode(product, "VH1-1").GetProperty("isActive").GetBoolean().ShouldBeFalse();

        using var on = await client.PostAsync($"{Products}/{id}/variants/{variantId}/activate", null);
        on.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        product = await (await client.GetAsync($"{Products}/{id}")).JsonAsync();
        VariantByCode(product, "VH1-1").GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }
}
