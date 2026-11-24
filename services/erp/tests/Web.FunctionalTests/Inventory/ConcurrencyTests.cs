using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

/// <summary>Gerçek paralel HTTP istekleri, gerçek PostgreSQL: her istek kendi kapsam, bağlantı ve işlemini kullanır.</summary>
public class ConcurrencyTests : TestBase {
    private const int Parallel = 8;

    private static async Task<(HttpStatusCode Status, string? Code)[]> RunAll(IEnumerable<Func<Task<HttpResponseMessage>>> calls) {
        var tasks = calls.Select(call => Task.Run(async () => {
                                                      using var response = await call();
                                                      var code = response.IsSuccessStatusCode ? null : await response.ErrorCodeAsync();
                                                      return (response.StatusCode, code);
                                                  }));
        return await Task.WhenAll(tasks);
    }

    private static IEnumerable<Func<Task<HttpResponseMessage>>> Repeat(int count, Func<int, Task<HttpResponseMessage>> call) {
        return Enumerable.Range(0, count).Select<int, Func<Task<HttpResponseMessage>>>(i => () => call(i));
    }

    [Test]
    public async Task ConcurrentLastUnit_OnlyOneSucceeds() {
        for (var round = 0; round < 3; round++) {
            var env = await NewEnv(code: "R" + round);
            await Receive(env, 1m);

            var results = await RunAll(Repeat(Parallel, _ => Issue(env, 1m, D3)));

            results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
            results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
            (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(0m);
            (await Movements(env.Tenant)).Sum(m => m.Quantity).ShouldBe(0m);
        }
    }

    [Test]
    public async Task ConcurrentReserveLastUnit_OnlyOneSucceeds() {
        var env = await NewEnv();
        await Receive(env, 1m);

        var results = await RunAll(Repeat(Parallel, _ => Op(env.Client, "reserve", ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                                                              RLine(env.Warehouse, env.Product, 1m, Guid.CreateVersion7())))));

        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
        results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
        (await Reservations(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentReserveAndIssue_NeverOversells() {
        for (var round = 0; round < 3; round++) {
            var env = await NewEnv(code: "V" + round);
            await Receive(env, 1m);

            var results = await RunAll(Repeat(Parallel, i => i % 2 == 0
                                                          ? Op(env.Client, "reserve", ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                                                                 RLine(env.Warehouse, env.Product, 1m, Guid.CreateVersion7())))
                                                          : Issue(env, 1m, D3)));

            results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
            results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
            var physical = (await Movements(env.Tenant)).Sum(m => m.Quantity);
            var reserved = (await Reservations(env.Tenant)).Sum(r => r.Quantity);
            (physical - reserved).ShouldBeGreaterThanOrEqualTo(0m);
        }
    }

    [Test]
    public async Task ConcurrentReceipts_KeepAverageSnapshotConsistent() {
        var env = await NewEnv();

        var results = await RunAll(Repeat(Parallel, i => Op(env.Client, "receive", Doc("Receipt", D1, Line(env.Warehouse, env.Product, 2m, 10m + i)))));

        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        await using var db  = PricingTestSupport.Db(env.Tenant);
        var             all = db.AverageCosts.ToList();
        all.Count.ShouldBe(1);
        all[0].Quantity.ShouldBe(2m * Parallel);

        // Sapma yarış değil yuvarlamadır: her adım 6 haneye yuvarlandığından sonuç girişlerin sırasına bağlıdır. Hareketler
        // Sequence sırasıyla (kilit altında yazıldıkları sıra) yeniden oynatılınca anlık değerle birebir aynı çıkmalıdır;
        // bir giriş kaybolsaydı ya da iki giriş aynı bakiyeyi okusaydı bu eşitlik bozulurdu.
        var replay = (Qty: 0m, Avg: 0m);
        foreach (var m in db.StockMovements.OrderBy(m => m.Sequence).ToList()) {
            var (qty, avg) = ERP.Domain.Inventory.WeightedAverage.ApplyInbound(replay.Qty, replay.Avg, m.Quantity, m.UnitCost);
            replay = (qty, Math.Round(avg, 6, MidpointRounding.AwayFromZero));
        }

        all[0].AverageCost.ShouldBe(replay.Avg);
        all[0].AverageCost.ShouldBe(13.5m, 0.00001m); // sıraya göre yuvarlama sapması en çok birkaç mikro
    }

    [Test]
    public async Task ConcurrentMultiLineDocuments_InOppositeOrder_DoNotDeadlock() {
        var env = await NewEnv();
        var b   = await CreateProduct(env.Client, "P2");
        await Receive(env, 100m);
        await Receive(env, 100m, product: b);

        var results = await RunAll(Repeat(Parallel, i => Op(env.Client, "issue", Doc("Issue", D3,
                                                                                    i % 2 == 1
                                                                                        ? [Line(env.Warehouse, b, 1m), Line(env.Warehouse, env.Product, 1m)]
                                                                                        : [Line(env.Warehouse, env.Product, 1m), Line(env.Warehouse, b, 1m)]))));

        results.ShouldAllBe(r => r.Status == HttpStatusCode.OK);
        (await Movements(env.Tenant)).Count(m => m.Quantity < 0).ShouldBe(Parallel * 2);
    }

    [Test]
    public async Task ConcurrentFifoIssues_NeverConsumeSameLayerTwice() {
        var env = await NewEnv();
        await SetStock(env.Client, "fifo");
        await Receive(env, 4m, 10m);

        var results = await RunAll(Repeat(Parallel, _ => Issue(env, 1m, D3)));

        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(4);
        await using var db = PricingTestSupport.Db(env.Tenant);
        db.CostLayers.Single().RemainingQty.ShouldBe(0m);
    }
}
