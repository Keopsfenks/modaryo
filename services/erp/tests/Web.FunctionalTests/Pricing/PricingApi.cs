using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Pricing;

/// <summary>Fiyat testleri için ortak istemci ve kısayollar.</summary>
internal static class PricingApi {
    public const string Base = "/api/erp/pricing";

    public static string[] Permissions =>
        ["katalog.read", "katalog.manage", "cari.read", "cari.manage", "fiyat.read", "fiyat.manage", "fiyat.alis", "ayarlar.fiyat"];

    public static Task<HttpClient> Client(Guid tenant, params string[] permissions) {
        return TestEnvironment.Factory.ClientAsync(tenant, null, null, null, permissions.Length == 0 ? Permissions : permissions);
    }

    public static object[] Tiers(params (decimal Min, decimal Price)[] tiers) {
        return tiers.Select(t => (object)new { minQty = t.Min, price = t.Price }).ToArray();
    }

    public static async Task<Guid> CreateList(HttpClient client, string name, string kind = "sale", object? extra = null) {
        var body = new Dictionary<string, object?> { ["name"] = name, ["kind"] = kind, ["currency"] = "TRY", ["vatIncluded"] = true };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        using var response = await client.PostAsJsonAsync($"{Base}/lists", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateProduct(HttpClient client, string code, object? extra = null) {
        var body = new Dictionary<string, object?> { ["kind"] = "product", ["name"] = "Ürün " + code, ["code"] = code };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        return (await CreateOk(client, body)).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PutRows(HttpClient client, Guid listId, params object[] rows) {
        return client.PutAsJsonAsync($"{Base}/lists/{listId}/items", new { rows });
    }

    public static object Row(Guid productId, object[] tiers, uint? version = null, string unit = "adet", Guid? variantId = null) {
        return new { productId, variantId, unit, tiers, version };
    }

    public static async Task<JsonElement[]> Items(HttpClient client, Guid listId, string query = "") {
        using var response = await client.GetAsync($"{Base}/lists/{listId}/items{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("items").EnumerateArray().ToArray();
    }

    public static async Task<JsonElement[]> History(HttpClient client, string query = "") {
        using var response = await client.GetAsync($"{Base}/history{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("items").EnumerateArray().ToArray();
    }

    public static decimal FirstPrice(JsonElement item) {
        return item.GetProperty("tiers")[0].GetProperty("price").GetDecimal();
    }

    public static async Task SetSettings(HttpClient client, int version, object value) {
        using var response = await client.PutAsJsonAsync("/api/erp/settings/fiyat", new { version, value });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
