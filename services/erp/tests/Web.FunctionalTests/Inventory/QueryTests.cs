using System.Text.Json;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class QueryTests : TestBase {
    private static readonly DateOnly D10 = new(2026, 3, 10);
    private static readonly DateOnly D12 = new(2026, 3, 12);

    private static async Task<JsonElement> Get(HttpClient client, string url) {
        using var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpClient> Limited(Guid tenant, string[] permissions, Guid[]? branches = null) {
        return TestEnvironment.Factory.ClientAsync(tenant, null, branches, null, permissions);
    }

    [Test]
    public async Task Balances_ShowPhysicalReservedAvailable() {
        var env = await NewEnv();
        await Receive(env, 10m, 8m);
        await Ok(Op(env.Client, "reserve", ReserveDoc("order", Guid.CreateVersion7(), RLine(env.Warehouse, env.Product, 3m, Guid.CreateVersion7()))));

        var page = await Get(env.Client, $"{Base}/balances?warehouseId={env.Warehouse}");
        page.GetProperty("total").GetInt32().ShouldBe(1);
        var row = page.GetProperty("items")[0];
        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("reserved").GetDecimal().ShouldBe(3m);
        row.GetProperty("available").GetDecimal().ShouldBe(7m);
        row.GetProperty("avgCost").GetDecimal().ShouldBe(8m);
        row.GetProperty("productCode").GetString().ShouldBe("P1");
    }

    [Test]
    public async Task Balances_FiltersSearchNonZeroAndBelowMinimum() {
        var env   = await NewEnv();
        var other = await CreateProduct(env.Client, "ZZ-9");
        await Receive(env, 2m);
        await Receive(env, 5m, product: other);
        await Ok(Issue(env, 5m, D10, other));
        await AddMinLevel(env.Tenant, env.Warehouse, env.Product, 5m);

        (await Get(env.Client, $"{Base}/balances?search=zz-")).GetProperty("total").GetInt32().ShouldBe(1);
        (await Get(env.Client, $"{Base}/balances")).GetProperty("total").GetInt32().ShouldBe(2);
        (await Get(env.Client, $"{Base}/balances?onlyNonZero=true")).GetProperty("total").GetInt32().ShouldBe(1);

        var below = await Get(env.Client, $"{Base}/balances?belowMinimum=true");
        below.GetProperty("total").GetInt32().ShouldBe(1);
        below.GetProperty("items")[0].GetProperty("minQty").GetDecimal().ShouldBe(5m);
    }

    [Test]
    public async Task Balances_NoCostWithoutMaliyetRead() {
        var env = await NewEnv();
        await Receive(env, 10m, 8m);
        var reader = await Limited(env.Tenant, ["stok.read"]);

        var row = (await Get(reader, $"{Base}/balances")).GetProperty("items")[0];
        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("avgCost").ValueKind.ShouldBe(JsonValueKind.Null);

        var card = await Get(reader, $"{Base}/item-card?productId={env.Product}&warehouseId={env.Warehouse}");
        var move = card.GetProperty("movements")[0];
        move.GetProperty("unitCost").ValueKind.ShouldBe(JsonValueKind.Null);
        move.GetProperty("totalCost").ValueKind.ShouldBe(JsonValueKind.Null);

        using var valuation = await reader.GetAsync($"{Base}/valuation");
        valuation.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ItemCard_RunningQuantityAndOpening() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 4m, D5));
        await Receive(env, 2m, 10m, D10);

        var card = await Get(env.Client, $"{Base}/item-card?productId={env.Product}&warehouseId={env.Warehouse}&from=2026-03-03");
        card.GetProperty("openingQty").GetDecimal().ShouldBe(10m);
        var rows = card.GetProperty("movements").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        rows[0].GetProperty("type").GetString().ShouldBe("issue");
        rows[0].GetProperty("out").GetDecimal().ShouldBe(4m);
        rows[0].GetProperty("running").GetDecimal().ShouldBe(6m);
        rows[0].GetProperty("sourceType").GetString().ShouldBe("test");
        rows[0].GetProperty("sourceId").GetGuid().ShouldNotBe(Guid.Empty);
        rows[1].GetProperty("in").GetDecimal().ShouldBe(2m);
        rows[1].GetProperty("running").GetDecimal().ShouldBe(8m);
        rows[1].GetProperty("unitCost").GetDecimal().ShouldBe(10m);
        card.GetProperty("closingQty").GetDecimal().ShouldBe(8m);

        var upTo = await Get(env.Client, $"{Base}/item-card?productId={env.Product}&warehouseId={env.Warehouse}&to=2026-03-05");
        upTo.GetProperty("openingQty").GetDecimal().ShouldBe(0m);
        upTo.GetProperty("movements").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task Valuation_AtPastDate() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Receive(env, 10m, 20m, D10);
        await Ok(Issue(env, 5m, D12));

        var early = await Get(env.Client, $"{Base}/valuation?date=2026-03-05&warehouseId={env.Warehouse}");
        early.GetProperty("totalValue").GetDecimal().ShouldBe(100m);
        var item = early.GetProperty("items")[0];
        item.GetProperty("quantity").GetDecimal().ShouldBe(10m);
        item.GetProperty("unitCost").GetDecimal().ShouldBe(10m);

        var late = await Get(env.Client, $"{Base}/valuation?date=2026-03-20");
        late.GetProperty("items")[0].GetProperty("quantity").GetDecimal().ShouldBe(15m);
        late.GetProperty("totalValue").GetDecimal().ShouldBe(225m); // 100 + 200 - 5 x ortalama 15
    }

    [Test]
    public async Task Balances_OnlyAccessibleBranches() {
        var tenant = Guid.CreateVersion7();
        var b1     = Guid.CreateVersion7();
        var b2     = Guid.CreateVersion7();
        SeedBranches(tenant, Branch(b1, "Merkez", "MRK"), Branch(b2, "Şube", "SB2"));
        var admin = await AdminClient(tenant);
        var w1    = await DefaultWarehouse(admin, b1);
        var w2    = await DefaultWarehouse(admin, b2);
        var item  = await CreateProduct(admin, "P1");
        var env   = new Env(tenant, admin, b1, w1, item);
        await Receive(env, 3m);
        await Receive(env, 4m, warehouse: w2);

        (await Get(admin, $"{Base}/balances")).GetProperty("total").GetInt32().ShouldBe(2);

        var scoped = await Limited(tenant, ["stok.read"], [b1]);
        var page   = await Get(scoped, $"{Base}/balances");
        page.GetProperty("total").GetInt32().ShouldBe(1);
        page.GetProperty("items")[0].GetProperty("warehouseId").GetGuid().ShouldBe(w1);

        var valuation = await Limited(tenant, ["stok.read", "maliyet.read"], [b1]);
        (await Get(valuation, $"{Base}/valuation")).GetProperty("items").GetArrayLength().ShouldBe(1);

        using var denied = await scoped.GetAsync($"{Base}/item-card?productId={item}&warehouseId={w2}");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
