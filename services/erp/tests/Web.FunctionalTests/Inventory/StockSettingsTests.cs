using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class StockSettingsTests : TestBase {
    private const           string Stok    = "/api/erp/settings/stok";
    private static readonly Guid   Tenant  = Guid.CreateVersion7();
    private static readonly Guid   BranchA = Guid.CreateVersion7();

    [Test]
    public async Task Defaults_AreWeightedAverageAndNoNegativeStock() {
        using var client = await Client(Tenant);

        var body = await (await client.GetAsync(Stok)).JsonAsync();

        body.GetProperty("version").GetInt32().ShouldBe(0);
        body.GetProperty("value").GetProperty("method").GetString().ShouldBe("weightedAverage");
        body.GetProperty("value").GetProperty("allowNegative").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task RequiresSettingsPermission() {
        using var client = await TestEnvironment.Factory.ClientAsync(Tenant, null, null, null, ["stok.read", "stok.manage"]);

        (await client.GetAsync(Stok)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ChangeCostingMethodBeforeMovement_Succeeds() {
        using var client = await Client(Tenant);

        using var put = await client.PutAsJsonAsync(Stok, new { version = 0, value = new { method = "fifo", allowNegative = true } });

        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync(Stok)).JsonAsync()).GetProperty("value").GetProperty("method").GetString().ShouldBe("fifo");
    }

    [Test]
    public async Task ChangeCostingMethodAfterMovement_Returns409() {
        SeedBranches(Tenant, Branch(BranchA));
        using var client    = await Client(Tenant);
        var       warehouse = await DefaultWarehouse(client, BranchA);
        var       product   = await CreateProduct(client, "S1");
        await AddMovementAsync(Tenant, warehouse, product);

        using var blocked = await client.PutAsJsonAsync(Stok, new { version = 0, value = new { method = "fifo", allowNegative = false } });

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("costing_method_locked");

        // Yöntem aynı kalırsa (yalnızca eksi stok ayarı) değişiklik serbesttir.
        using var allowed = await client.PutAsJsonAsync(Stok, new { version = 0, value = new { method = "weightedAverage", allowNegative = true } });
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
