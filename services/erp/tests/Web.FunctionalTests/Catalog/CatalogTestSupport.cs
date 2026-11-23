using System.Net.Http.Json;
using System.Text.Json;

namespace ERP.Web.FunctionalTests.Catalog;

internal static class CatalogTestSupport {
    public const string Products = "/api/erp/catalog/products";

    public static string[] AllPermissions => ["katalog.read", "katalog.manage", "ayarlar.katalog", "cari.read", "cari.manage"];

    public static Task<HttpClient> Manager(Guid tenant, string[]? modules = null) {
        return TestEnvironment.Factory.ClientAsync(tenant, modules, null, null, AllPermissions);
    }

    public static async Task<JsonElement> CreateOk(HttpClient client, object body) {
        using var response = await client.PostAsJsonAsync(Products, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<Guid> CreateSupplier(HttpClient client, string code, bool supplier) {
        using var response = await client.PostAsJsonAsync("/api/erp/current-accounts", new {
            code, name = "Tedarikçi " + code, kind = "corporate", isCustomer = !supplier, isSupplier = supplier,
            currency = "TRY", defaultDueDays = 30
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }
}
