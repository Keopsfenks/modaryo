using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Platform;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

/// <summary>Stok servisini test uçları (StockProbes) üzerinden gerçek HTTP istekleriyle çağırır.</summary>
internal static class StockTestSupport {
    public const string Probe = "/test-probe/stock";

    public static readonly DateOnly D1 = new(2026, 3, 1);
    public static readonly DateOnly D3 = new(2026, 3, 3);
    public static readonly DateOnly D5 = new(2026, 3, 5);

    public sealed record Env(Guid Tenant, HttpClient Client, Guid Branch, Guid Warehouse, Guid Product);

    public static async Task<Env> NewEnv(string[]? modules = null, string code = "P1") {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        SeedBranches(tenant, Branch(branch));
        var client = await AdminClient(tenant, modules);
        return new Env(tenant, client, branch, await DefaultWarehouse(client, branch), await CreateProduct(client, code));
    }

    public static Task<HttpClient> AdminClient(Guid tenant, string[]? modules = null) {
        return TestEnvironment.Factory.ClientAsync(tenant, modules, null, null, [..Permissions, "ayarlar.finans"]);
    }

    public static Task<HttpResponseMessage> Op(HttpClient client, string op, object body) {
        return client.PostAsJsonAsync($"{Probe}?op={op}", body);
    }

    public static object Line(Guid warehouse, Guid product, decimal quantity, decimal? cost = null, Guid? variant = null,
                              Guid? lineId = null, bool exempt = false) {
        return new {
            warehouseId = warehouse, productId = product, variantId = variant, baseQuantity = quantity, unitCost = cost,
            sourceLineId = lineId, exemptFromStockCheck = exempt
        };
    }

    public static object Doc(string type, DateOnly date, params object[] lines) {
        return new { type, sourceType = "test", sourceId = Guid.CreateVersion7(), date = date.ToString("yyyy-MM-dd"), lines };
    }

    public static async Task Ok(Task<HttpResponseMessage> request) {
        using var response = await request;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static Task Receive(Env env, decimal quantity, decimal? cost = 10m, DateOnly? date = null, Guid? warehouse = null,
                               Guid? product = null) {
        return Ok(Op(env.Client, "receive", Doc("Receipt", date ?? D1, Line(warehouse ?? env.Warehouse, product ?? env.Product, quantity, cost))));
    }

    public static Task<HttpResponseMessage> Issue(Env env, decimal quantity, DateOnly? date = null, Guid? product = null,
                                                  Guid? warehouse = null, object? doc = null) {
        return Op(env.Client, "issue",
                  doc ?? Doc("Issue", date ?? D5, Line(warehouse ?? env.Warehouse, product ?? env.Product, quantity)));
    }

    public static async Task Fail(HttpResponseMessage response, HttpStatusCode status, string code) {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await response.ErrorCodeAsync()).ShouldBe(code);
    }

    public static async Task<JsonElement> Balance(Env env, Guid? warehouse = null, Guid? product = null) {
        using var response = await Op(env.Client, "balance", new { items = new[] { new { productId = product ?? env.Product, variantId = (Guid?)null } }, warehouseId = warehouse });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> BalanceOf(Env env, Guid warehouse, Guid? product = null) {
        var rows = await Balance(env, warehouse, product);
        rows.GetArrayLength().ShouldBe(1);
        return rows[0];
    }

    public static async Task<List<StockMovement>> Movements(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.StockMovements.AsNoTracking().OrderBy(m => m.Sequence).ToListAsync();
    }

    public static async Task<List<Reservation>> Reservations(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.Reservations.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    public static async Task<List<RevaluationJob>> Jobs(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.RevaluationJobs.AsNoTracking().ToListAsync();
    }

    public static async Task<int> OutboxCount(Guid tenant, string type) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.OutboxMessages.CountAsync(m => m.Type == type);
    }

    public static async Task SetStock(HttpClient client, string method = "weightedAverage", bool allowNegative = false, int version = 0) {
        using var put = await client.PutAsJsonAsync("/api/erp/settings/stok", new { version, value = new { method, allowNegative } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }

    public static async Task AddMinLevel(Guid tenant, Guid warehouse, Guid product, decimal min) {
        await using var db = PricingTestSupport.Db(tenant);
        db.MinStockLevels.Add(new MinStockLevel { WarehouseId = warehouse, ProductId = product, MinQty = min });
        await db.SaveChangesAsync();
    }

    public static object ReserveDoc(string sourceType, Guid sourceId, params object[] lines) {
        return new { sourceType, sourceId, lines };
    }

    public static object RLine(Guid warehouse, Guid product, decimal quantity, Guid lineId, bool exempt = false, Guid? expected = null) {
        return new {
            warehouseId = warehouse, productId = product, variantId = (Guid?)null, baseQuantity = quantity, sourceLineId = lineId,
            exemptFromStockCheck = exempt, expectedReceiptSourceId = expected
        };
    }
}
