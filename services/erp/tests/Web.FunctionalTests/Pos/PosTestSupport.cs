using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Treasury;

namespace ERP.Web.FunctionalTests.Pos;

internal static class PosTestSupport {
    public const string Api = "/api/erp/pos";

    public static readonly string[] Permissions = [
        "pos.sell", "pos.return", "pos.shift", "pos.cashmove", "pos.reports", "pos.manage", "ayarlar.pos",
        "finans.read", "finans.manage", "ayarlar.ebelge", "ayarlar.finans", "ayarlar.stok", "katalog.read", "katalog.manage",
        "cari.read", "cari.manage", "cari.movements", "stok.read", "stok.manage", "fiyat.read", "fiyat.manage", "maliyet.read"
    ];

    public sealed record Env(
        Guid Tenant, Guid Branch, Guid OtherBranch, HttpClient Client, Guid Warehouse, Guid OtherWarehouse, Guid CashAccount, Guid CardAccount);

    /// <summary>İki şubeli firma: Merkez (kasa hesapları ve deposuyla) ve Kadıköy.</summary>
    public static async Task<Env> NewEnv(string[]? permissions = null, string[]? modules = null) {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var other  = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(tenant, new BranchRef(branch, "MRK", "Merkez", true, true), new BranchRef(other, "KDK", "Kadıköy", true, false));
        var client    = await TestEnvironment.Factory.ClientAsync(tenant, modules, null, null, permissions ?? Permissions);
        var cash      = (await TreasuryTestSupport.CreateAccount(client, branch, "Merkez Kasa")).GetProperty("id").GetGuid();
        var card      = (await TreasuryTestSupport.CreateAccount(client, branch, "Merkez POS", "cardPos", bankName: "Test Bank")).GetProperty("id").GetGuid();
        var warehouse = await InventoryTestSupport.DefaultWarehouse(client, branch);
        var otherWh   = await InventoryTestSupport.DefaultWarehouse(client, other);
        return new Env(tenant, branch, other, client, warehouse, otherWh, cash, card);
    }

    public static Dictionary<string, object?> RegisterBody(Env env, string code = "K1", object? extra = null) {
        var body = new Dictionary<string, object?> {
            ["code"] = code, ["name"] = "Kasa " + code, ["branchId"] = env.Branch, ["warehouseId"] = env.Warehouse,
            ["cashAccountId"] = env.CashAccount, ["cardAccountId"] = env.CardAccount
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static async Task<JsonElement> CreateRegister(Env env, string code = "K1") {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, code));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> GetSettings(HttpClient client) {
        using var response = await client.GetAsync("/api/erp/settings/pos");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<HttpResponseMessage> PutSettings(HttpClient client, object value) {
        var current = await GetSettings(client);
        return await client.PutAsJsonAsync("/api/erp/settings/pos", new { version = current.GetProperty("version").GetInt32(), value });
    }

    public static async Task SetScaleFormats(HttpClient client) {
        using var response = await PutSettings(client, new {
            scaleBarcodes = new object[] {
                new { prefix = "27", itemCodeLength = 5, valueLength = 5, valueKind = "weightGrams" },
                new { prefix = "28", itemCodeLength = 5, valueLength = 5, valueKind = "priceKurus" }
            }
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<Guid> Product(HttpClient client, string code, object? extra = null, decimal price = 100m, bool vatIncluded = false) {
        var body = new Dictionary<string, object?> {
            ["kind"] = "product", ["name"] = "Ürün " + code, ["code"] = code,
            ["vatRate"] = 20, ["baseUnit"] = "adet", ["defaultSalePrice"] = new { amount = price, currency = "TRY", vatIncluded }
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return (await Catalog.CatalogTestSupport.CreateOk(client, body)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> Lookup(HttpClient client, string code, HttpStatusCode expected = HttpStatusCode.OK, Guid? registerId = null) {
        using var response = await client.GetAsync($"{Api}/lookup?code={Uri.EscapeDataString(code)}" + (registerId is { } r ? $"&registerId={r}" : ""));
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }
}
