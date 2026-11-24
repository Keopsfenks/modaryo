using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

public class HistoryTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Money(decimal amount) {
        return new { amount, currency = "TRY", vatIncluded = true };
    }

    [Test]
    public async Task CardDefaultPriceChange_WritesHistory() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1", new { defaultSalePrice = Money(100m) });

        using var update = await client.PutAsJsonAsync($"/api/erp/catalog/products/{product}", new {
            kind = "product", name = "Ürün P1", code = "P1", defaultSalePrice = Money(120m), defaultPurchasePrice = Money(60m)
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        // Fiyat değişmeden kaydetmek geçmişe yazmaz.
        (await client.PutAsJsonAsync($"/api/erp/catalog/products/{product}", new {
            kind = "product", name = "Ürün P1 yeni", code = "P1", defaultSalePrice = Money(120m), defaultPurchasePrice = Money(60m)
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var history = await History(client, $"?productId={product}&scope=card");

        history.Length.ShouldBe(2);
        var sale = history.Single(h => h.GetProperty("kind").GetString() == "sale");
        sale.GetProperty("before").GetProperty("amount").GetDecimal().ShouldBe(100m);
        sale.GetProperty("after").GetProperty("amount").GetDecimal().ShouldBe(120m);
        sale.GetProperty("scope").GetString().ShouldBe("card");
        history.Single(h => h.GetProperty("kind").GetString() == "purchase").GetProperty("before").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task VariantOverrideChange_WritesHistory() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "V1", new {
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } }
        });
        var created = await (await client.GetAsync($"/api/erp/catalog/products/{product}")).JsonAsync();
        var variant = created.GetProperty("variants")[0];
        var id      = variant.GetProperty("id").GetGuid();
        var code    = variant.GetProperty("code").GetString();

        async Task Put(object? sale, object? purchase) {
            using var response = await client.PutAsJsonAsync($"/api/erp/catalog/products/{product}/variants/{id}",
                                                             new { code, salePriceOverride = sale, purchasePriceOverride = purchase });
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        await Put(Money(100m), null);
        await Put(Money(80m), Money(40m));
        await Put(Money(80m), Money(40m)); // değişmedi: yeni kayıt yok

        var history = await History(client, $"?productId={product}&scope=card");

        history.Length.ShouldBe(3);
        history.ShouldAllBe(h => h.GetProperty("variantId").GetGuid() == id);
        var sale = history.Where(h => h.GetProperty("kind").GetString() == "sale").ToArray();
        sale.Length.ShouldBe(2);
        sale.ShouldContain(h => h.GetProperty("before").ValueKind == JsonValueKind.Object
                                && h.GetProperty("before").GetProperty("amount").GetDecimal() == 100m
                                && h.GetProperty("after").GetProperty("amount").GetDecimal() == 80m);
        history.Count(h => h.GetProperty("kind").GetString() == "purchase").ShouldBe(1);
    }

    [Test]
    public async Task PurchaseHistory_HiddenWithoutAlis() {
        using var admin   = await Client(Tenant);
        var       product = await CreateProduct(admin, "P1");
        (await admin.PutAsJsonAsync($"/api/erp/catalog/products/{product}", new {
            kind = "product", name = "Ürün P1", code = "P1", defaultSalePrice = Money(10m), defaultPurchasePrice = Money(5m)
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await CreateList(admin, "Alış", "purchase");
        await PutRows(admin, list, Row(product, Tiers((1, 5m))));

        using var reader = await Client(Tenant, "fiyat.read");

        (await History(admin, $"?productId={product}")).Length.ShouldBe(3);
        var visible = await History(reader, $"?productId={product}");
        visible.Length.ShouldBe(1);
        visible[0].GetProperty("kind").GetString().ShouldBe("sale");
    }
}
