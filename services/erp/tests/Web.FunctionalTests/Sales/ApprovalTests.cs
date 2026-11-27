using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class ApprovalTests : TestBase {
    private const int Parallel = 8;

    private static async Task<(HttpStatusCode Status, string? Code)[]> RunAll(IEnumerable<Func<Task<HttpResponseMessage>>> calls) {
        var tasks = calls.Select(call => Task.Run(async () => {
                                                      using var response = await call();
                                                      var code = response.IsSuccessStatusCode ? null : await response.ErrorCodeAsync();
                                                      return (response.StatusCode, code);
                                                  }));
        return await Task.WhenAll(tasks);
    }

    [Test]
    public async Task Approve_ReservesStock() {
        var env       = await NewOrderEnv();
        var warehouse = await Stock(env, 10m);
        var id        = await NewOrder(env, [QLine(env.Product, 4m, 50m)]);

        var order = await Approve(env.Client, id);

        Status(order).ShouldBe("approved");
        var reservation = (await Reservations(env)).ShouldHaveSingleItem();
        reservation.Quantity.ShouldBe(4m);
        reservation.WarehouseId.ShouldBe(warehouse);
        reservation.Status.ShouldBe(ReservationStatus.Active);
        reservation.SourceId.ShouldBe(id);
        order.GetProperty("reservations").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Approve_InsufficientStock_Returns409WithLines() {
        var env = await NewOrderEnv();
        await Stock(env, 3m);
        var id = await NewOrder(env, [QLine(env.Product, 5m, 50m)]);

        using var response = await ApproveRaw(env.Client, id);

        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
        var body = await response.JsonAsync();
        var line = body.GetProperty("details")[0];
        line.GetProperty("required").GetDecimal().ShouldBe(5m);
        line.GetProperty("available").GetDecimal().ShouldBe(3m);
        (await Reservations(env)).ShouldBeEmpty();
        Status(await GetOrder(env.Client, id)).ShouldBe("draft");
    }

    [Test]
    public async Task Approve_InsufficientStock_NegativeAllowed_WarnsAndApproves() {
        var env = await NewOrderEnv();
        await Stock(env, 3m);
        await StockTestSupport.SetStock(env.Client, allowNegative: true);
        var id = await NewOrder(env, [QLine(env.Product, 5m, 50m)]);

        var order = await Approve(env.Client, id);

        Status(order).ShouldBe("approved");
        order.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain("insufficient_stock");
    }

    [Test]
    public async Task Approve_ExposureIncludesOpenOrders() {
        var env = await NewOrderEnv();
        await Stock(env, 20m);
        await SetCreditLimit(env, 10000m);
        await Debit(env, 6000m);
        var first  = await NewOrder(env, [QLine(env.Product, 1m, 3000m, new { vatIncluded = true })]);
        var second = await NewOrder(env, [QLine(env.Product, 1m, 2000m, new { vatIncluded = true })]);

        Status(await Approve(env.Client, first)).ShouldBe("approved"); // 6.000 + 3.000 = 9.000
        var waiting = await Approve(env.Client, second);               // 6.000 + 3.000 + 2.000 = 11.000 > 10.000

        Status(waiting).ShouldBe("awaitingLimitApproval");
        (await Reservations(env)).Count.ShouldBe(1);
        var approved = await Post(env.Client, $"orders/{second}/approve-limit");
        Status(approved).ShouldBe("approved");
        (await Reservations(env)).Count.ShouldBe(2);
    }

    [Test]
    public async Task ApproveLimit_RequiresPermission() {
        var env = await NewOrderEnv();
        await Stock(env, 20m);
        await SetCreditLimit(env, 100m);
        var id = await NewOrder(env, [QLine(env.Product, 1m, 500m)]);
        Status(await Approve(env.Client, id)).ShouldBe("awaitingLimitApproval");
        var limited = await OtherClient(env, "satis.read", "satis.manage", "satis.approve", "cari.read", "stok.read");

        using var denied = await limited.PostAsJsonAsync($"{Api}/orders/{id}/approve-limit", new { });
        var       ok     = await Post(env.Client, $"orders/{id}/approve-limit");

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Status(ok).ShouldBe("approved");
    }

    [Test]
    public async Task ConcurrentApprovals_LastUnit_NeverOversell() {
        for (var round = 0; round < 2; round++) {
            var env       = await NewOrderEnv();
            var warehouse = await Stock(env, 1m);
            var ids       = new List<Guid>();
            for (var i = 0; i < Parallel; i++) ids.Add(await NewOrder(env, [QLine(env.Product, 1m, 50m)]));

            var results = await RunAll(ids.Select<Guid, Func<Task<HttpResponseMessage>>>(id => () => ApproveRaw(env.Client, id)));

            results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
            results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
            var reservations = await Reservations(env);
            reservations.Count(r => r.Status == ReservationStatus.Active).ShouldBe(1);
            reservations.Where(r => r.Status == ReservationStatus.Active).Sum(r => r.Quantity).ShouldBeLessThanOrEqualTo(1m);
            (await Orders(env)).Count(o => o.Status == ERP.Domain.Sales.SalesOrderStatus.Draft).ShouldBe(Parallel - 1);
            await using var db = PricingTestSupport.Db(env.Tenant);
            (await db.StockMovements.Where(m => m.WarehouseId == warehouse).SumAsync(m => m.Quantity)).ShouldBe(1m);
        }
    }

    [Test]
    public async Task ConcurrentApprovals_PartialStock_ReservedNeverExceedsPhysical() {
        var env = await NewOrderEnv();
        await Stock(env, 5m);
        var ids = new List<Guid>();
        for (var i = 0; i < Parallel; i++) ids.Add(await NewOrder(env, [QLine(env.Product, 2m, 50m)]));

        var results = await RunAll(ids.Select<Guid, Func<Task<HttpResponseMessage>>>(id => () => ApproveRaw(env.Client, id)));

        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(2);
        results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
        (await Reservations(env)).Where(r => r.Status == ReservationStatus.Active).Sum(r => r.Quantity).ShouldBe(4m);
    }

    [Test]
    public async Task ConcurrentApprovals_OppositeLineOrder_NoDeadlock() {
        var env = await NewOrderEnv();
        var b   = await PricingApi.CreateProduct(env.Client, "P2", new { vatRate = 20, baseUnit = "adet" });
        await Stock(env, 1m);
        await Stock(env, 1m, b);
        var ids = new List<Guid>();
        for (var i = 0; i < Parallel; i++)
            ids.Add(await NewOrder(env, i % 2 == 0 ? [QLine(env.Product, 1m, 50m), QLine(b, 1m, 50m)] : [QLine(b, 1m, 50m), QLine(env.Product, 1m, 50m)]));

        var results = await RunAll(ids.Select<Guid, Func<Task<HttpResponseMessage>>>(id => () => ApproveRaw(env.Client, id)));

        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
        results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "insufficient_stock");
        (await Reservations(env)).Count(r => r.Status == ReservationStatus.Active).ShouldBe(2);
    }

    [Test]
    public async Task ConcurrentApprovals_SameOrder_OneReservationSet() {
        var env = await NewOrderEnv();
        await Stock(env, 10m);
        var id = await NewOrder(env, [QLine(env.Product, 2m, 50m)]);

        var results = await RunAll(Enumerable.Range(0, Parallel).Select<int, Func<Task<HttpResponseMessage>>>(_ => () => ApproveRaw(env.Client, id)));

        results.Count(r => r.Status == HttpStatusCode.OK).ShouldBe(1);
        results.Where(r => r.Status != HttpStatusCode.OK).ShouldAllBe(r => r.Status == HttpStatusCode.Conflict && r.Code == "order_not_draft");
        (await Reservations(env)).Count.ShouldBe(1);
    }
}
