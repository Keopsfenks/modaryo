using System.Text.Json;
using ERP.Application.Inventory.Costing;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class CostingTests : TestBase {
    private static readonly DateOnly D0 = new(2026, 2, 1);
    private static readonly DateOnly D7 = new(2026, 3, 7);

    private static Task<int> RunWorker(Env env) {
        return TestEnvironment.Factory.Services.GetRequiredService<RevaluationWorker>().PublicRunOnceAsync(CancellationToken.None, env.Tenant);
    }

    private static async Task<List<StockMovement>> Issues(Guid tenant) {
        return (await Movements(tenant)).Where(m => m.Quantity < 0m).OrderBy(m => m.Date).ThenBy(m => m.Sequence).ToList();
    }

    private static async Task<AverageCostSnapshot> Snapshot(Guid tenant, Guid warehouse) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.AverageCosts.AsNoTracking().SingleAsync(s => s.WarehouseId == warehouse);
    }

    [Test]
    public async Task IssueCost_WeightedAverage() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m);
        await Receive(env, 10m, 20m);

        await Ok(Issue(env, 5m, D5));

        var issue = (await Issues(env.Tenant)).Single();
        issue.UnitCost.ShouldBe(15m);
        issue.TotalCost.ShouldBe(-75m);

        using var scope = TestEnvironment.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new BackgroundTenant(env.Tenant);
        var calculator = scope.ServiceProvider.GetRequiredService<CostCalculator>();
        (await calculator.IssueUnitCostAsync(env.Warehouse, env.Product, null, D5, long.MaxValue, 5m, CancellationToken.None)).ShouldBe(15m);
        (await calculator.IssueUnitCostAsync(env.Warehouse, env.Product, null, D1, 0, 5m, CancellationToken.None)).ShouldBe(0m);
    }

    [Test]
    public async Task IssueCost_Fifo() {
        var env = await NewEnv();
        await SetStock(env.Client, "fifo");
        await Receive(env, 10m, 10m, D1);
        await Receive(env, 10m, 20m, D3);

        await Ok(Issue(env, 15m, D5));

        var issue = (await Issues(env.Tenant)).Single();
        issue.TotalCost.ShouldBe(-200m); // 10 x 10 + 5 x 20
        issue.UnitCost.ShouldBe(13.333333m);

        using var scope = TestEnvironment.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().Override = new BackgroundTenant(env.Tenant);
        var calculator = scope.ServiceProvider.GetRequiredService<CostCalculator>();
        (await calculator.IssueUnitCostAsync(env.Warehouse, env.Product, null, D3, 0, 10m, CancellationToken.None)).ShouldBe(10m);
    }

    [Test]
    public async Task Revaluation_WeightedAverage_RecostsLaterIssue() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 5m, D5));
        (await Issues(env.Tenant)).Single().TotalCost.ShouldBe(-50m);

        await Receive(env, 10m, 20m, D3); // geriye dönük giriş
        (await Jobs(env.Tenant)).Single().FromDate.ShouldBe(D3);

        (await RunWorker(env)).ShouldBe(1);

        var issue = (await Issues(env.Tenant)).Single();
        issue.UnitCost.ShouldBe(15m);
        issue.TotalCost.ShouldBe(-75m);
        (await Jobs(env.Tenant)).ShouldBeEmpty();
        var snapshot = await Snapshot(env.Tenant, env.Warehouse);
        snapshot.Quantity.ShouldBe(15m);
        snapshot.AverageCost.ShouldBe(15m);
    }

    [Test]
    public async Task Revaluation_Fifo_ConsumesEarlierLayer() {
        var env = await NewEnv();
        await SetStock(env.Client, "fifo");
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 5m, D5));
        (await Issues(env.Tenant)).Single().TotalCost.ShouldBe(-50m);

        await Receive(env, 10m, 20m, D0); // D1 girişinden önce
        (await RunWorker(env)).ShouldBe(1);

        (await Issues(env.Tenant)).Single().TotalCost.ShouldBe(-100m); // önce D0 katmanı tüketilir
        await using var db     = PricingTestSupport.Db(env.Tenant);
        var             layers = await db.CostLayers.AsNoTracking().OrderBy(l => l.Date).ToListAsync();
        layers.Select(l => l.RemainingQty).ShouldBe([5m, 10m]);
        layers.Select(l => l.UnitCost).ShouldBe([20m, 10m]);
        var snapshot = await Snapshot(env.Tenant, env.Warehouse);
        snapshot.Quantity.ShouldBe(15m);
        snapshot.AverageCost.ShouldBe(13.333333m); // (5 x 20 + 10 x 10) / 15
    }

    [Test]
    public async Task Revaluation_StopsAtPeriodLock() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 2m, D5));
        await Ok(Issue(env, 2m, D7));
        await Receive(env, 10m, 20m, D3);

        // Yeniden değerleme koşmadan önce dönem D5'e kadar kilitlenir.
        using var put = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { periodLockDate = "2026-03-05" } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        await RunWorker(env);

        var issues = await Issues(env.Tenant);
        issues[0].TotalCost.ShouldBe(-20m); // kilitli dönemde: dokunulmaz
        issues[0].UnitCost.ShouldBe(10m);
        issues[1].UnitCost.ShouldBe(15m);   // kilit sonrası: yeni ortalama
        issues[1].TotalCost.ShouldBe(-30m);
    }

    [Test]
    public async Task Revaluation_MergesJobsToEarliestDate() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 1m, D7));

        await Receive(env, 1m, 11m, D3);
        await Receive(env, 1m, 12m, D0);
        await Receive(env, 1m, 13m, D5);

        var job = (await Jobs(env.Tenant)).Single();
        job.FromDate.ShouldBe(D0);
        (await RunWorker(env)).ShouldBe(1);
        (await Jobs(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Transfer_CarriesSourceCost() {
        var env   = await NewEnv();
        var other = await CreateWarehouse(env.Client, env.Branch, "B2");
        await Receive(env, 10m, 10m, D1);
        await Receive(env, 10m, 30m, D1);

        await Ok(Op(env.Client, "transfer", new {
            sourceType = "transfer", sourceId = Guid.CreateVersion7(), date = D5.ToString("yyyy-MM-dd"),
            lines = new[] { new { fromWarehouseId = env.Warehouse, toWarehouseId = other, productId = env.Product, variantId = (Guid?)null, baseQuantity = 5m, lots = (object?)null, serials = (object?)null, sourceLineId = (Guid?)null } }
        }));

        var movements = await Movements(env.Tenant);
        var outbound  = movements.Single(m => m.Type == MovementType.TransferOut);
        var inbound   = movements.Single(m => m.Type == MovementType.TransferIn);
        outbound.UnitCost.ShouldBe(20m);
        inbound.UnitCost.ShouldBe(20m);
        inbound.WarehouseId.ShouldBe(other);
        (await Snapshot(env.Tenant, other)).AverageCost.ShouldBe(20m);

        // Kaynakta geriye dönük giriş: çıkış ve karşı depodaki giriş birlikte yeniden maliyetlenir.
        await Receive(env, 10m, 50m, D3);
        (await RunWorker(env)).ShouldBe(2); // kaynak + yayılan hedef işi

        movements = await Movements(env.Tenant);
        movements.Single(m => m.Type == MovementType.TransferOut).UnitCost.ShouldBe(30m); // (10x10+10x30+10x50)/30
        var recosted = movements.Single(m => m.Type == MovementType.TransferIn);
        recosted.UnitCost.ShouldBe(30m);
        recosted.TotalCost.ShouldBe(150m);
        (await Snapshot(env.Tenant, other)).AverageCost.ShouldBe(30m);
        (await Jobs(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Revaluation_EnqueuesCostRevaluedEvent() {
        var env = await NewEnv();
        await Receive(env, 10m, 10m, D1);
        await Ok(Issue(env, 5m, D5));
        await Receive(env, 10m, 20m, D3);

        await RunWorker(env);

        (await OutboxCount(env.Tenant, "stock.cost-revalued")).ShouldBe(1);
        await using var db = PricingTestSupport.Db(env.Tenant);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Type == "stock.cost-revalued");
        using var json = JsonDocument.Parse(message.PayloadJson);
        json.RootElement.GetProperty("old_unit_cost").GetDecimal().ShouldBe(10m);
        json.RootElement.GetProperty("new_unit_cost").GetDecimal().ShouldBe(15m);
        json.RootElement.GetProperty("new_total_cost").GetDecimal().ShouldBe(-75m);

        // Maliyet değişmediyse ikinci tur olay üretmez.
        await Receive(env, 1m, 15m, D3);
        await RunWorker(env);
        (await OutboxCount(env.Tenant, "stock.cost-revalued")).ShouldBe(1);
    }
}
