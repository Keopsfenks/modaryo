using System.Net.Http.Json;
using ERP.Domain.Catalog;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class BarcodeTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Product(string code, object? extra = null) {
        var body = new Dictionary<string, object?> { ["kind"] = "product", ["name"] = "Ürün " + code, ["code"] = code };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        return body;
    }

    [Test]
    public async Task ProductBarcodeTaken_Returns409() {
        using var client = await Manager(Tenant);
        var first = await CreateOk(client, Product("A1", new { barcode = "8690001" }));
        first.GetProperty("barcode").GetString().ShouldBe("8690001");

        using var duplicate = await client.PostAsJsonAsync(Products, Product("A2", new { barcode = "8690001" }));

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).ShouldBe("barcode_taken");

        // Kendi barkodunu güncellerken çakışma sayılmaz; başka kartın barkodu sayılır.
        var id = first.GetProperty("id").GetGuid();
        (await client.PutAsJsonAsync($"{Products}/{id}", Product("A1", new { barcode = "8690001" }))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var second = (await CreateOk(client, Product("A3", new { barcode = "8690003" }))).GetProperty("id").GetGuid();
        using var steal = await client.PutAsJsonAsync($"{Products}/{second}", Product("A3", new { barcode = "8690001" }));
        steal.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Barkod bırakılınca başkası alabilir.
        (await client.PutAsJsonAsync($"{Products}/{id}", Product("A1"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"{Products}/{second}", Product("A3", new { barcode = "8690001" }))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task UnitBarcodeEqualsOtherVariantBarcode_Returns409() {
        using var client = await Manager(Tenant);
        await CreateOk(client, Product("V1", new {
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } },
            variants   = new[] { new { code = "V1-S", barcode = "8691111", values = new Dictionary<string, string> { ["Beden"] = "S" } } }
        }));

        using var response = await client.PostAsJsonAsync(Products, Product("V2", new {
            units = new[] { new { unit = "koli", factor = 10m, barcode = "8691111" } }
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("barcode_taken");
    }

    [Test]
    public async Task SameBarcodeTwiceInRequest_Returns409() {
        using var client = await Manager(Tenant);

        using var productAndUnit = await client.PostAsJsonAsync(Products, Product("T1", new {
            barcode = "8692222", units = new[] { new { unit = "koli", factor = 10m, barcode = "8692222" } }
        }));
        using var twoUnits = await client.PostAsJsonAsync(Products, Product("T2", new {
            units = new[] {
                new { unit = "koli", factor = 10m, barcode = "8692223" },
                new { unit = "paket", factor = 5m, barcode = "8692223" }
            }
        }));

        productAndUnit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await productAndUnit.ErrorCodeAsync()).ShouldBe("barcode_taken");
        twoUnits.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await twoUnits.ErrorCodeAsync()).ShouldBe("barcode_taken");
        (await (await client.GetAsync(Products)).JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task GenerateBarcode_ValidEan13WithTenantPrefix() {
        using var client = await Manager(Tenant);
        var       id     = (await CreateOk(client, Product("G1"))).GetProperty("id").GetGuid();

        var first  = await (await client.PostAsync($"{Products}/{id}/barcode/generate", null)).JsonAsync();
        var second = await (await client.PostAsync($"{Products}/{id}/barcode/generate", null)).JsonAsync();

        var a = first.GetProperty("barcode").GetString()!;
        var b = second.GetProperty("barcode").GetString()!;
        Ean13.IsValid(a).ShouldBeTrue();
        a.ShouldStartWith("2000000");
        a.ShouldBe(Ean13.Build("2000000", 1));
        b.ShouldBe(Ean13.Build("2000000", 2));

        // Elle girilmiş bir barkod üretilecek sıradaysa atlanır.
        var taken = Ean13.Build("2000000", 3);
        await CreateOk(client, Product("G2", new { barcode = taken }));
        var third = await (await client.PostAsync($"{Products}/{id}/barcode/generate", null)).JsonAsync();
        third.GetProperty("barcode").GetString().ShouldBe(Ean13.Build("2000000", 4));
    }

    [Test]
    public async Task GenerateBarcode_UsesSettingsPrefix() {
        using var client = await Manager(Tenant);
        var       id     = (await CreateOk(client, Product("G9"))).GetProperty("id").GetGuid();
        var       get    = await (await client.GetAsync("/api/erp/settings/katalog")).JsonAsync();
        using var put = await client.PutAsJsonAsync("/api/erp/settings/katalog", new {
            version = get.GetProperty("version").GetInt32(), value = new { barcodePrefix = "2123456", nextBarcodeSequence = 77 }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await (await client.PostAsync($"{Products}/{id}/barcode/generate", null)).JsonAsync();

        body.GetProperty("barcode").GetString().ShouldBe(Ean13.Build("2123456", 77));
    }
}
