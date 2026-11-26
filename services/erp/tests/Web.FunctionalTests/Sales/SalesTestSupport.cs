using System.Text.Json;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

internal static class SalesTestSupport {
    public const string Api = "/api/erp/sales";

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    public static readonly string[] Permissions = [
        .. AllPermissions, "satis.read", "satis.manage", "satis.approve", "satis.limit-asimi", "satis.ship", "satis.return", "satis.recurring", "satis.targets",
        "ayarlar.satis", "fiyat.read", "fiyat.manage", "maliyet.read"
    ];

    public static Task<Env> NewSalesEnv(string[]? permissions = null, decimal? maxDiscount = null, string[]? modules = null) {
        return NewEnv(permissions ?? Permissions, maxDiscount, modules: modules);
    }

    public static Dictionary<string, object?> QLine(Guid product, decimal quantity = 2m, decimal? price = 50m, object? extra = null) {
        var line = new Dictionary<string, object?> { ["productId"] = product, ["quantity"] = quantity, ["unitPrice"] = price };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                line[property.Name] = property.GetValue(extra);
        return line;
    }

    public static Dictionary<string, object?> QuoteBody(Env env, object[]? lines = null, object? extra = null, DateOnly? date = null) {
        var body = new Dictionary<string, object?> {
            ["currentAccountId"] = env.Customer, ["branchId"] = env.Branch, ["date"] = (date ?? Day).ToString("yyyy-MM-dd"), ["currency"] = "TRY",
            ["lines"] = lines ?? [QLine(env.Product)]
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static async Task<JsonElement> CreateQuote(Env env, Dictionary<string, object?> body) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/quotes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> Post(HttpClient client, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) {
        using var response = await client.PostAsJsonAsync($"{Api}/{path}", body ?? new { });
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.NoContent ? default : await response.JsonAsync();
    }

    public static async Task<JsonElement> GetQuote(HttpClient client, Guid id, int? revision = null) {
        using var response = await client.GetAsync($"{Api}/quotes/{id}" + (revision is { } r ? $"?revision={r}" : ""));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task SetSalesSettings(HttpClient client, object value) {
        using var current = await client.GetAsync("/api/erp/settings/satis");
        var doc     = await current.JsonAsync();
        var version = doc.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
        using var put = await client.PutAsJsonAsync("/api/erp/settings/satis", new { version, value });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }
}
