using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class LedgerTests : TestBase {
    private static object Correction(Env env, Guid inboundId, string date, decimal delta) {
        return new {
            warehouseId = env.Warehouse, productId = env.Product, variantId = (Guid?)null, date, inboundMovementId = inboundId,
            deltaTotalTl = delta, sourceType = "invoice", sourceId = Guid.CreateVersion7()
        };
    }

    private static async Task<Guid> CreateSet(Env env, string code, params (Guid Product, decimal Quantity)[] components) {
        var body = new Dictionary<string, object?> {
            ["kind"] = "product", ["name"] = "Set " + code, ["code"] = code, ["isBundle"] = true,
            ["bundleComponents"] = components.Select(c => new { productId = c.Product, variantId = (Guid?)null, quantity = c.Quantity }).ToArray()
        };
        return (await CreateOk(env.Client, body)).GetProperty("id").GetGuid();
    }

    [Test]
    public async Task Receipt_IncreasesPhysicalAndAvailable() {
        var env = await NewEnv();
        await Receive(env, 10m, 5m);

        var row = await BalanceOf(env, env.Warehouse);

        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("available").GetDecimal().ShouldBe(10m);
        row.GetProperty("reserved").GetDecimal().ShouldBe(0m);
        row.GetProperty("averageCost").GetDecimal().ShouldBe(5m);
        var movement = (await Movements(env.Tenant)).Single();
        movement.Quantity.ShouldBe(10m);
        movement.TotalCost.ShouldBe(50m);
        movement.Sequence.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task Issue_InsufficientStock_Returns409() {
        var env = await NewEnv();
        await Receive(env, 3m);

        using var response = await Issue(env, 5m);

        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
        (await Movements(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Issue_CostsAtWeightedAverage() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m);
        await Receive(env, 10m, 20m, D3);

        await Ok(Issue(env, 5m));

        var issue = (await Movements(env.Tenant)).Last();
        issue.Quantity.ShouldBe(-5m);
        issue.UnitCost.ShouldBe(15m);
        issue.TotalCost.ShouldBe(-75m);
        (await BalanceOf(env, env.Warehouse)).GetProperty("averageCost").GetDecimal().ShouldBe(15m);
    }

    [Test]
    public async Task BackdatedIssue_MakesLaterBalanceNegative_Rejected() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 8m, D5));

        // 3. günde 10 - 5 = 5 geçerli, ama 5. günde 5 - 8 = -3 olur.
        using var rejected = await Issue(env, 5m, D3);
        await Fail(rejected, HttpStatusCode.Conflict, "insufficient_stock");

        // 10 - 2 = 8, 5. günde 8 - 8 = 0: geçerli. Geriye dönük olduğu için yeniden değerleme işi açılır.
        await Ok(Issue(env, 2m, D3));
        (await Jobs(env.Tenant)).Single().FromDate.ShouldBe(D3);
    }

    [Test]
    public async Task AllowNegative_PermitsIssue() {
        var env = await NewEnv();
        await SetStock(env.Client, allowNegative: true);

        await Ok(Issue(env, 5m));

        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(-5m);
    }

    [Test]
    public async Task Reverse_WritesOppositeMovements() {
        var env      = await NewEnv();
        var sourceId = Guid.CreateVersion7();
        await Ok(Op(env.Client, "receive", new {
            type = "Receipt", sourceType = "doc", sourceId, date = "2026-03-01", lines = new[] { Line(env.Warehouse, env.Product, 10m, 4m) }
        }));

        await Ok(Op(env.Client, "reverse", new { sourceType = "doc", sourceId, date = "2026-03-02" }));
        await Ok(Op(env.Client, "reverse", new { sourceType = "doc", sourceId, date = "2026-03-02" })); // ikinci çağrı yeni hareket yazmaz

        var movements = await Movements(env.Tenant);
        movements.Count.ShouldBe(2);
        movements.Sum(m => m.Quantity).ShouldBe(0m);
        movements[1].Quantity.ShouldBe(-10m);
        movements[1].TotalCost.ShouldBe(-40m);
    }

    [Test]
    public async Task Reverse_OfIssue_RestoresStockAtOriginalCost() {
        var env      = await NewEnv();
        var sourceId = Guid.CreateVersion7();
        await Receive(env, 10m, 6m);
        await Ok(Op(env.Client, "issue", new {
            type = "Issue", sourceType = "doc", sourceId, date = "2026-03-02", lines = new[] { Line(env.Warehouse, env.Product, 4m) }
        }));

        await Ok(Op(env.Client, "reverse", new { sourceType = "doc", sourceId, date = "2026-03-03" }));

        var row = await BalanceOf(env, env.Warehouse);
        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("averageCost").GetDecimal().ShouldBe(6m);
    }

    [Test]
    public async Task Reverse_OfReceipt_AlreadyConsumed_Returns409() {
        var env      = await NewEnv();
        var sourceId = Guid.CreateVersion7();
        await Ok(Op(env.Client, "receive", new {
            type = "Receipt", sourceType = "doc", sourceId, date = "2026-03-01", lines = new[] { Line(env.Warehouse, env.Product, 10m, 4m) }
        }));
        await Ok(Issue(env, 6m, D3));

        using var response = await Op(env.Client, "reverse", new { sourceType = "doc", sourceId, date = "2026-03-04" });

        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
    }

    [Test]
    public async Task BundleIssue_ExplodesComponents() {
        var env = await NewEnv(code: "CA");
        var b   = await CreateProduct(env.Client, "CB");
        var set = await CreateSet(env, "SET", (env.Product, 2m), (b, 1m));
        await Receive(env, 10m, 5m);
        await Receive(env, 10m, 8m, product: b);

        await Ok(Issue(env, 0m, doc: Doc("SalesShipment", D5, Line(env.Warehouse, set, 3m))));

        var movements = await Movements(env.Tenant);
        var issues    = movements.Where(m => m.Type == MovementType.BundleIssue).ToList();
        issues.Count.ShouldBe(2);
        issues.Single(m => m.ProductId == env.Product).Quantity.ShouldBe(-6m);
        issues.Single(m => m.ProductId == b).Quantity.ShouldBe(-3m);
        movements.Any(m => m.ProductId == set).ShouldBeFalse();
    }

    [Test]
    public async Task BundleInbound_Returns409() {
        var env = await NewEnv(code: "CA");
        var set = await CreateSet(env, "SET", (env.Product, 1m));

        using var response = await Op(env.Client, "receive", Doc("Receipt", D1, Line(env.Warehouse, set, 1m, 5m)));

        await Fail(response, HttpStatusCode.Conflict, "bundle_inbound");
    }

    [Test]
    public async Task QuarantineReceipt_NotAvailable() {
        var env        = await NewEnv();
        var quarantine = await CreateWarehouse(env.Client, env.Branch, "KRN", "Quarantine");
        await Receive(env, 5m, 10m, warehouse: quarantine);

        var row = await BalanceOf(env, quarantine);

        row.GetProperty("physical").GetDecimal().ShouldBe(5m);
        row.GetProperty("available").GetDecimal().ShouldBe(0m);
        (await Balance(env, env.Warehouse)).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task BelowMinimum_EnqueuesEventOnceWhenCrossing() {
        var env = await NewEnv();
        await AddMinLevel(env.Tenant, env.Warehouse, env.Product, 5m);
        await Receive(env, 10m);

        await Ok(Issue(env, 3m, D3)); // 7
        (await OutboxCount(env.Tenant, "stock.below-minimum")).ShouldBe(0);
        await Ok(Issue(env, 3m, D3)); // 4: eşik aşıldı
        (await OutboxCount(env.Tenant, "stock.below-minimum")).ShouldBe(1);
        await Ok(Issue(env, 1m, D3)); // 3: zaten altında
        (await OutboxCount(env.Tenant, "stock.below-minimum")).ShouldBe(1);
    }

    [Test]
    public async Task PeriodLocked_Returns409() {
        var env = await NewEnv();
        using var put = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { periodLockDate = "2026-01-31" } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        using var response = await Op(env.Client, "receive", Doc("Receipt", new DateOnly(2026, 1, 15), Line(env.Warehouse, env.Product, 1m, 5m)));

        await Fail(response, HttpStatusCode.Conflict, "period_locked");
        (await Movements(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task BackdatedReceipt_EnqueuesJobMergedToEarliestDate() {
        var env = await NewEnv();
        await Receive(env, 5m, 10m, D5);
        await Receive(env, 5m, 12m, D3);
        await Receive(env, 5m, 14m, D1);

        var job = (await Jobs(env.Tenant)).Single();

        job.FromDate.ShouldBe(D1);
    }

    [Test]
    public async Task CorrectCost_WeightedAverage_RaisesAvgAndRecostsLaterIssue() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m);
        var inbound = (await Movements(env.Tenant)).Single();

        await Ok(Op(env.Client, "correct", Correction(env, inbound.Id, "2026-03-02", 20m)));

        var movements   = await Movements(env.Tenant);
        var revaluation = movements.Single(m => m.Type == MovementType.Revaluation);
        revaluation.Quantity.ShouldBe(0m);
        revaluation.TotalCost.ShouldBe(20m);
        (await BalanceOf(env, env.Warehouse)).GetProperty("averageCost").GetDecimal().ShouldBe(12m);
        (await Jobs(env.Tenant)).Single().FromDate.ShouldBe(new DateOnly(2026, 3, 2));

        await Ok(Issue(env, 2m, D3));
        (await Movements(env.Tenant)).Last().TotalCost.ShouldBe(-24m);
    }

    [Test]
    public async Task CorrectCost_Fifo_RaisesLayerCost() {
        var env = await NewEnv();
        await SetStock(env.Client, "fifo");
        await Receive(env, 10m, 10m);
        var inbound = (await Movements(env.Tenant)).Single();

        await Ok(Op(env.Client, "correct", Correction(env, inbound.Id, "2026-03-02", 20m)));

        await using (var db = PricingTestSupport.Db(env.Tenant))
            db.CostLayers.Single().UnitCost.ShouldBe(12m);

        await Ok(Issue(env, 5m, D3));
        (await Movements(env.Tenant)).Last().TotalCost.ShouldBe(-60m);
        (await Jobs(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task CorrectCost_AfterStockWasFullyIssued_IsAccepted() {
        // Düzeltme girişin konumunda uygulanır; günün sonunda stok kalmamış olması engel değildir (cost_correction_no_stock artık atılmaz).
        var env = await NewEnv();
        await Receive(env, 10m, 10m);
        await Ok(Issue(env, 10m, D3));
        var inbound = (await Movements(env.Tenant)).First();

        await Ok(Op(env.Client, "correct", Correction(env, inbound.Id, "2026-03-04", 20m)));

        (await Movements(env.Tenant)).Count(m => m.Type == MovementType.Revaluation).ShouldBe(1);
    }

    [Test]
    public async Task Fifo_IssueConsumesOldestLayerFirst() {
        var env = await NewEnv();
        await SetStock(env.Client, "fifo");
        await Receive(env, 5m, 10m, D1);
        await Receive(env, 5m, 20m, D3);

        await Ok(Issue(env, 7m, D5));

        // 5 x 10 + 2 x 20
        (await Movements(env.Tenant)).Last().TotalCost.ShouldBe(-90m);
        (await BalanceOf(env, env.Warehouse)).GetProperty("averageCost").GetDecimal().ShouldBe(20m);
    }
}
