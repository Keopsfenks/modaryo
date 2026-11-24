using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

public class CampaignTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static async Task<JsonElement[]> Evaluate(HttpClient client, object[] lines, string? at = null) {
        using var response = await client.PostAsJsonAsync($"{Base}/campaigns/evaluate", new { lines, at });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("results").EnumerateArray().ToArray();
    }

    private static async Task<JsonElement> CreateCampaign(HttpClient client, object body) {
        using var response = await client.PostAsJsonAsync($"{Base}/campaigns", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    [Test]
    public async Task CreateBuyXPayY_InvalidYGreaterOrEqualX_Returns400() {
        using var client = await Client(Tenant);
        var       p      = await CreateProduct(client, "K1");

        foreach (var (x, y) in new[] { (3, 3), (2, 5), (3, 0) }) {
            using var response = await client.PostAsJsonAsync($"{Base}/campaigns", new {
                name = "Geçersiz", kind = "BuyXPayY", buyQty = x, payQty = y, productIds = new[] { p }
            });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{x}/{y}");
        }

        using var percent = await client.PostAsJsonAsync($"{Base}/campaigns", new {
            name = "Yüzde", kind = "PercentOff", percent = 120m, productIds = new[] { p }
        });
        percent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Evaluate_CategoryScopedCampaignAppliesToChildCategoryProduct() {
        using var client = await Client(Tenant);
        var parent = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Giyim" })).JsonAsync())
            .GetProperty("id").GetGuid();
        var child = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Erkek", parentId = parent })).JsonAsync())
            .GetProperty("id").GetGuid();
        var inChild  = await CreateProduct(client, "K2", new { categoryId = child });
        var outside  = await CreateProduct(client, "K3");
        await CreateCampaign(client, new { name = "Giyim %10", kind = "PercentOff", percent = 10m, categoryIds = new[] { parent } });

        var results = await Evaluate(client, [
            new { productId = inChild, quantity = 2m, unitPrice = 100m },
            new { productId = outside, quantity = 2m, unitPrice = 100m }
        ]);

        results[0].GetProperty("discountAmount").GetDecimal().ShouldBe(20m);
        results[0].GetProperty("name").GetString().ShouldBe("Giyim %10");
        results[1].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Evaluate_BestSingleCampaignWins_NoStacking() {
        using var client = await Client(Tenant);
        var       p      = await CreateProduct(client, "K4");
        await CreateCampaign(client, new { name = "3 al 2 öde", kind = "BuyXPayY", buyQty = 3, payQty = 2, productIds = new[] { p } });
        await CreateCampaign(client, new { name = "%50", kind = "PercentOff", percent = 50m, productIds = new[] { p } });

        var results = await Evaluate(client, [new { productId = p, quantity = 3m, unitPrice = 10m }]);

        results[0].GetProperty("name").GetString().ShouldBe("%50");
        results[0].GetProperty("discountAmount").GetDecimal().ShouldBe(15m);
    }

    [Test]
    public async Task Evaluate_TimeWindowUsesIstanbulTime() {
        using var client = await Client(Tenant);
        var       p      = await CreateProduct(client, "K5");
        // Salı 09:00-12:00 (İstanbul).
        await CreateCampaign(client, new {
            name = "Sabah", kind = "PercentOff", percent = 10m, productIds = new[] { p },
            days = new[] { "Tuesday" }, fromTime = "09:00:00", toTime = "12:00:00"
        });
        object[] line = [new { productId = p, quantity = 1m, unitPrice = 100m }];

        // 07:00 UTC = 10:00 İstanbul: içeride (UTC olsa dışarıda kalırdı).
        (await Evaluate(client, line, "2026-03-03T07:00:00Z"))[0].ValueKind.ShouldBe(JsonValueKind.Object);
        // 10:00 UTC = 13:00 İstanbul: dışarıda (UTC olsa içeride kalırdı).
        (await Evaluate(client, line, "2026-03-03T10:00:00Z"))[0].ValueKind.ShouldBe(JsonValueKind.Null);
        // Doğru saat, yanlış gün (Çarşamba).
        (await Evaluate(client, line, "2026-03-04T07:00:00Z"))[0].ValueKind.ShouldBe(JsonValueKind.Null);
        // 22:00 UTC Salı = 01:00 İstanbul Çarşamba.
        (await Evaluate(client, line, "2026-03-03T22:00:00Z"))[0].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task InactiveCampaignIgnored() {
        using var client = await Client(Tenant);
        var       p      = await CreateProduct(client, "K6");
        var       created = await CreateCampaign(client, new { name = "Pasif", kind = "PercentOff", percent = 10m, productIds = new[] { p }, isActive = false });

        (await Evaluate(client, [new { productId = p, quantity = 1m, unitPrice = 100m }]))[0].ValueKind.ShouldBe(JsonValueKind.Null);

        using var listed = await client.GetAsync($"{Base}/campaigns");
        (await listed.JsonAsync()).EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == created.GetProperty("id").GetGuid())
            .ShouldBeFalse();
    }

    [Test]
    public async Task UpdateAndDelete_AndPermissions() {
        using var client = await Client(Tenant);
        var       p      = await CreateProduct(client, "K7");
        var       created = await CreateCampaign(client, new { name = "A", kind = "PercentOff", percent = 10m, productIds = new[] { p } });
        var       id     = created.GetProperty("id").GetGuid();

        using var reader = await Client(Tenant, "fiyat.read");
        (await reader.PostAsJsonAsync($"{Base}/campaigns", new { name = "B", kind = "PercentOff", percent = 5m, productIds = new[] { p } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Base}/campaigns")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var update = await client.PutAsJsonAsync($"{Base}/campaigns/{id}", new { name = "A2", kind = "PercentOff", percent = 20m, productIds = new[] { p } });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        (await update.JsonAsync()).GetProperty("percent").GetDecimal().ShouldBe(20m);

        (await client.DeleteAsync($"{Base}/campaigns/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"{Base}/campaigns/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
