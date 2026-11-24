using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

internal static class InventoryTestSupport {
    public const string Base = "/api/erp/inventory";

    public static string[] Permissions =>
        ["katalog.read", "katalog.manage", "stok.read", "stok.manage", "stok.sayim", "maliyet.read", "ayarlar.stok"];

    public static Task<HttpClient> Client(Guid tenant, Guid[]? branchIds = null, string[]? modules = null) {
        return TestEnvironment.Factory.ClientAsync(tenant, modules, branchIds, null, Permissions);
    }

    public static BranchRef Branch(Guid id, string name = "Merkez", string code = "MRK", bool isDefault = false) {
        return new BranchRef(id, code, name, true, isDefault);
    }

    public static void SeedBranches(Guid tenant, params BranchRef[] branches) {
        TestEnvironment.Factory.Branches.Seed(tenant, branches);
    }

    public static async Task<Guid> CreateProduct(HttpClient client, string code, object? extra = null) {
        var body = new Dictionary<string, object?> { ["kind"] = "product", ["name"] = "Ürün " + code, ["code"] = code };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);

        return (await CreateOk(client, body)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateWarehouse(HttpClient client, Guid branchId, string code, string kind = "Normal") {
        using var response = await client.PostAsJsonAsync($"{Base}/warehouses", new { branchId, code, name = "Depo " + code, kind });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> DefaultWarehouse(HttpClient client, Guid branchId) {
        using var response = await client.GetAsync($"{Base}/warehouses/default?branchId={branchId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Hareket yazan uç P3 üzerindeki sonraki görevlerde gelir; şimdilik doğrudan veritabanına yazılır.</summary>
    public static async Task AddMovementAsync(Guid tenant, Guid warehouseId, Guid productId, Guid? variantId = null, decimal quantity = 5m) {
        await using var db = PricingTestSupport.Db(tenant);
        db.StockMovements.Add(new StockMovement {
            WarehouseId = warehouseId, ProductId = productId, VariantId = variantId, Type = MovementType.Opening,
            Date = new DateOnly(2026, 1, 1), Quantity = quantity, UnitCost = 10m, TotalCost = quantity * 10m,
            SourceType = "opening", SourceId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, ActorLabel = "Test"
        });
        await db.SaveChangesAsync();
    }

    public static JsonElement Find(JsonElement array, Guid id) {
        return array.EnumerateArray().First(e => e.GetProperty("id").GetGuid() == id);
    }
}
