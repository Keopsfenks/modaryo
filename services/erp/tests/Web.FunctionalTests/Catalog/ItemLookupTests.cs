using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class ItemLookupTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private const string Items = "/api/erp/catalog/items";

    private static async Task<JsonElement[]> Search(HttpClient client, string query) {
        using var response = await client.GetAsync($"{Items}?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToArray();
    }

    private static object Money(decimal amount) {
        return new { amount, currency = "TRY", vatIncluded = true };
    }

    [Test]
    public async Task ProductWithVariants_ReturnsOnlyVariantItems() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new {
            kind = "product", name = "Tişört", code = "IT-A", defaultSalePrice = Money(100m),
            attributes = new[] {
                new { name = "Renk", values = new[] { "Kırmızı" } }, new { name = "Beden", values = new[] { "S", "M" } }
            }
        });
        await CreateOk(client, new { kind = "product", name = "Tişört düz", code = "IT-B" });

        var items = await Search(client, "search=IT-");

        items.Length.ShouldBe(3);
        var variants = items.Where(i => i.GetProperty("variantId").ValueKind != JsonValueKind.Null).ToArray();
        variants.Length.ShouldBe(2);
        variants.Select(v => v.GetProperty("name").GetString()).Order()
            .ShouldBe(["Tişört — Kırmızı / M", "Tişört — Kırmızı / S"]);
        variants[0].GetProperty("code").GetString().ShouldStartWith("IT-A-");
        var plain = items.Single(i => i.GetProperty("variantId").ValueKind == JsonValueKind.Null);
        plain.GetProperty("code").GetString().ShouldBe("IT-B");
        plain.GetProperty("name").GetString().ShouldBe("Tişört düz");
        plain.GetProperty("kind").GetString().ShouldBe("product");
        plain.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task Search_FoldsTurkishAndMatchesBarcodeAndSupplierCode() {
        using var client = await Manager(Tenant);
        var supplier = await CreateSupplier(client, "TDK-ARA", true);
        await CreateOk(client, new {
            kind = "product", name = "IŞIK LAMBA", code = "SR-1", barcode = "8697001",
            suppliers = new[] { new { currentAccountId = supplier, supplierCode = "TDK-4455", isDefault = true } }
        });
        await CreateOk(client, new { kind = "product", name = "Masa", code = "SR-2" });

        (await Search(client, "search=" + Uri.EscapeDataString("ışık"))).Select(i => i.GetProperty("code").GetString())
            .ShouldBe(["SR-1"]);
        (await Search(client, "search=" + Uri.EscapeDataString("IŞIK"))).Length.ShouldBe(1);
        (await Search(client, "search=8697001")).Select(i => i.GetProperty("code").GetString()).ShouldBe(["SR-1"]);
        (await Search(client, "search=tdk-4455")).Select(i => i.GetProperty("code").GetString()).ShouldBe(["SR-1"]);
        (await Search(client, "search=SR-2&kind=service")).ShouldBeEmpty();
        (await Search(client, "search=SR-&limit=1")).Length.ShouldBe(1);
    }

    [Test]
    public async Task BarcodeOfKoliUnit_ReturnsKoliAndFactor12() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new {
            kind = "product", name = "Su", code = "BK-1", barcode = "8698001",
            units = new[] { new { unit = "koli", factor = 12m, barcode = "8698012" } }
        });
        var withVariants = await CreateOk(client, new {
            kind = "product", name = "Gömlek", code = "BK-2",
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } }
        });
        var variantId = withVariants.GetProperty("variants")[0].GetProperty("id").GetGuid();
        using var set = await client.PutAsJsonAsync($"{Products}/{withVariants.GetProperty("id").GetGuid()}/variants/{variantId}",
                                                    new { code = "BK-2-S", barcode = "8698100" });
        set.StatusCode.ShouldBe(HttpStatusCode.OK);

        var koli = await (await client.GetAsync($"{Items}/barcode/8698012")).JsonAsync();
        koli.GetProperty("unit").GetString().ShouldBe("koli");
        koli.GetProperty("factor").GetDecimal().ShouldBe(12m);
        koli.GetProperty("item").GetProperty("code").GetString().ShouldBe("BK-1");
        koli.GetProperty("item").GetProperty("units").GetArrayLength().ShouldBe(1);

        var baseHit = await (await client.GetAsync($"{Items}/barcode/8698001")).JsonAsync();
        baseHit.GetProperty("unit").GetString().ShouldBe("adet");
        baseHit.GetProperty("factor").GetDecimal().ShouldBe(1m);

        var variantHit = await (await client.GetAsync($"{Items}/barcode/8698100")).JsonAsync();
        variantHit.GetProperty("item").GetProperty("variantId").GetGuid().ShouldBe(variantId);
        variantHit.GetProperty("item").GetProperty("name").GetString().ShouldBe("Gömlek — S");
        variantHit.GetProperty("unit").GetString().ShouldBe("adet");
    }

    [Test]
    public async Task VariantOverridePrice_Wins() {
        using var client = await Manager(Tenant);
        var created = await CreateOk(client, new {
            kind = "product", name = "Pantolon", code = "VP-1", defaultSalePrice = Money(200m),
            defaultPurchasePrice = Money(120m),
            attributes = new[] { new { name = "Beden", values = new[] { "S", "M" } } }
        });
        var s = created.GetProperty("variants").EnumerateArray().First(v => v.GetProperty("code").GetString() == "VP-1-1");
        using var update = await client.PutAsJsonAsync(
            $"{Products}/{created.GetProperty("id").GetGuid()}/variants/{s.GetProperty("id").GetGuid()}",
            new { code = "VP-1-1", salePriceOverride = Money(250m) });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var items = await Search(client, "search=VP-1");

        var overridden = items.Single(i => i.GetProperty("code").GetString() == "VP-1-1");
        overridden.GetProperty("salePrice").GetProperty("amount").GetDecimal().ShouldBe(250m);
        overridden.GetProperty("purchasePrice").GetProperty("amount").GetDecimal().ShouldBe(120m);
        var inherited = items.Single(i => i.GetProperty("code").GetString() == "VP-1-2");
        inherited.GetProperty("salePrice").GetProperty("amount").GetDecimal().ShouldBe(200m);
    }

    [Test]
    public async Task InactiveExcludedByDefault() {
        using var client = await Manager(Tenant);
        var passive = await CreateOk(client, new { kind = "product", name = "Eski", code = "IN-1" });
        var withVariants = await CreateOk(client, new {
            kind = "product", name = "Ceket", code = "IN-2",
            attributes = new[] { new { name = "Beden", values = new[] { "S", "M" } } }
        });
        using var off = await client.PostAsync($"{Products}/{passive.GetProperty("id").GetGuid()}/deactivate", null);
        off.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var productId = withVariants.GetProperty("id").GetGuid();
        var variantId = withVariants.GetProperty("variants")[0].GetProperty("id").GetGuid();
        using var offVariant = await client.PostAsync($"{Products}/{productId}/variants/{variantId}/deactivate", null);
        offVariant.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Search(client, "search=IN-")).Select(i => i.GetProperty("code").GetString()).ShouldBe(["IN-2-2"]);

        var all = await Search(client, "search=IN-&includeInactive=true");
        all.Length.ShouldBe(3);
        all.Count(i => !i.GetProperty("isActive").GetBoolean()).ShouldBe(2);
    }

    [Test]
    public async Task UnknownBarcode_Returns404() {
        using var client = await Manager(Tenant);
        // Varyantlı kartın ürün barkodu satılabilir bir kalem değildir.
        await CreateOk(client, new {
            kind = "product", name = "Kazak", code = "UB-1", barcode = "8699001",
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } }
        });

        using var unknown = await client.GetAsync($"{Items}/barcode/0000000");
        using var cardLevel = await client.GetAsync($"{Items}/barcode/8699001");

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        cardLevel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
