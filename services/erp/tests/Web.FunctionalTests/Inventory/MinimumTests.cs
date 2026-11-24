using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class MinimumTests : TestBase {
    private static object Row(Guid product, decimal min, decimal? reorder = null) {
        return new { productId = product, variantId = (Guid?)null, minQty = min, reorderQty = reorder };
    }

    [Test]
    public async Task MinimumsBelow_ListsItems() {
        var env   = await NewEnv();
        var other = await CreateProduct(env.Client, "P2");
        await Receive(env, 3m);
        await Receive(env, 50m, product: other);

        using var put = await env.Client.PutAsJsonAsync($"{Base}/minimums", new { warehouseId = env.Warehouse, rows = new[] { Row(env.Product, 5m, 20m), Row(other, 10m) } });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());

        using var all = await env.Client.GetAsync($"{Base}/minimums?warehouseId={env.Warehouse}");
        (await all.JsonAsync()).GetArrayLength().ShouldBe(2);

        using var below = await env.Client.GetAsync($"{Base}/minimums/below?warehouseId={env.Warehouse}");
        var       rows  = await below.JsonAsync();
        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("productId").GetGuid().ShouldBe(env.Product);
        rows[0].GetProperty("available").GetDecimal().ShouldBe(3m);
        rows[0].GetProperty("minQty").GetDecimal().ShouldBe(5m);
        rows[0].GetProperty("reorderQty").GetDecimal().ShouldBe(20m);
    }

    [Test]
    public async Task Minimums_PutReplacesRowsOfWarehouse() {
        var env   = await NewEnv();
        var other = await CreateProduct(env.Client, "P2");
        using var first = await env.Client.PutAsJsonAsync($"{Base}/minimums", new { warehouseId = env.Warehouse, rows = new[] { Row(env.Product, 5m) } });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var second = await env.Client.PutAsJsonAsync($"{Base}/minimums", new { warehouseId = env.Warehouse, rows = new[] { Row(other, 2m) } });
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var all  = await env.Client.GetAsync($"{Base}/minimums?warehouseId={env.Warehouse}");
        var       rows = await all.JsonAsync();
        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("productId").GetGuid().ShouldBe(other);

        using var duplicate = await env.Client.PutAsJsonAsync($"{Base}/minimums", new { warehouseId = env.Warehouse, rows = new[] { Row(other, 2m), Row(other, 3m) } });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
