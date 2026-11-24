using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class TransferTests : TestBase {
    private static object Transfer(Guid from, Guid to, Guid product, decimal quantity) {
        return new {
            date = "2026-03-02", fromWarehouseId = from, toWarehouseId = to,
            lines = new[] { new { productId = product, variantId = (Guid?)null, unit = "adet", quantity, lots = (object?)null, serials = (object?)null } }
        };
    }

    [Test]
    public async Task Transfer_MovesStockAndCarriesCost() {
        var env   = await NewEnv();
        var other = await CreateWarehouse(env.Client, env.Branch, "W2");
        await Receive(env, 10m, 8m);

        using var response = await env.Client.PostAsJsonAsync($"{Base}/transfers", Transfer(env.Warehouse, other, env.Product, 4m));
        var       created  = await DocumentSupport.Created(response);
        created.GetProperty("number").GetString()!.ShouldStartWith("TF2026");

        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(6m);
        var target = await BalanceOf(env, other);
        target.GetProperty("physical").GetDecimal().ShouldBe(4m);
        target.GetProperty("averageCost").GetDecimal().ShouldBe(8m);
        (await Movements(env.Tenant)).Select(m => m.Type.ToString()).ToArray().ShouldBe(["Receipt", "TransferOut", "TransferIn"]);

        using var list = await env.Client.GetAsync($"{Base}/transfers");
        (await list.JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Transfer_ToDamagedWarehouse_ReducesAvailable() {
        var env     = await NewEnv();
        var damaged = await CreateWarehouse(env.Client, env.Branch, "HAS", "Damaged");
        await Receive(env, 10m);

        using var response = await env.Client.PostAsJsonAsync($"{Base}/transfers", Transfer(env.Warehouse, damaged, env.Product, 3m));
        await DocumentSupport.Created(response);

        (await BalanceOf(env, env.Warehouse)).GetProperty("available").GetDecimal().ShouldBe(7m);
        var target = await BalanceOf(env, damaged);
        target.GetProperty("physical").GetDecimal().ShouldBe(3m);
        target.GetProperty("available").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Transfer_SameWarehouse_Returns400() {
        var env = await NewEnv();
        await Receive(env, 10m);

        using var response = await env.Client.PostAsJsonAsync($"{Base}/transfers", Transfer(env.Warehouse, env.Warehouse, env.Product, 1m));
        await Fail(response, HttpStatusCode.BadRequest, "same_warehouse");
    }

    [Test]
    public async Task Transfer_MoreThanStock_Returns409AndWritesNothing() {
        var env   = await NewEnv();
        var other = await CreateWarehouse(env.Client, env.Branch, "W2");
        await Receive(env, 2m);

        using var response = await env.Client.PostAsJsonAsync($"{Base}/transfers", Transfer(env.Warehouse, other, env.Product, 5m));
        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
        (await Movements(env.Tenant)).Count.ShouldBe(1);
    }
}
