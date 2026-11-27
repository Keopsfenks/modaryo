using ERP.Web.FunctionalTests.Treasury;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class PrepaymentTests : TestBase {
    [Test]
    public async Task Prepayment_AwaitingUntilAdvanceCovers() {
        var env     = await NewOrderEnv();
        await Stock(env, 10m);
        var account = await PaymentSupport.Account(env, "Kasa");
        var id      = await NewOrder(env, [QLine(env.Product, 2m, 50m)], new { requiresPrepayment = true, prepaymentPercent = 50m }); // toplam 120, ön ödeme 60

        var waiting = await Approve(env.Client, id);
        Status(waiting).ShouldBe("awaitingPayment");
        waiting.GetProperty("prepaymentRequired").GetDecimal().ShouldBe(60m);
        (await Reservations(env)).ShouldBeEmpty();

        await PaymentSupport.CollectOk(env, account, 30m, extra: new { isAdvance = true, salesOrderId = id });
        var partial = await GetOrder(env.Client, id);
        Status(partial).ShouldBe("awaitingPayment");
        partial.GetProperty("advanceTotal").GetDecimal().ShouldBe(30m);

        await PaymentSupport.CollectOk(env, account, 30m, extra: new { isAdvance = true, salesOrderId = id });
        var done = await GetOrder(env.Client, id);
        Status(done).ShouldBe("approved");
        done.GetProperty("advances").GetArrayLength().ShouldBe(2);
        (await Reservations(env)).ShouldHaveSingleItem().Quantity.ShouldBe(2m);
    }

    [Test]
    public async Task Prepayment_StockShort_CollectionStillRecordedOrderStaysAwaiting() {
        var env     = await NewOrderEnv();
        await Stock(env, 1m);
        var account = await PaymentSupport.Account(env, "Kasa");
        var id      = await NewOrder(env, [QLine(env.Product, 2m, 50m)], new { requiresPrepayment = true, prepaymentAmount = 60m });
        Status(await Approve(env.Client, id)).ShouldBe("awaitingPayment");

        await PaymentSupport.CollectOk(env, account, 60m, extra: new { isAdvance = true, salesOrderId = id });

        var order = await GetOrder(env.Client, id);
        Status(order).ShouldBe("awaitingPayment");
        order.GetProperty("advanceTotal").GetDecimal().ShouldBe(60m);
        (await Reservations(env)).ShouldBeEmpty();

        await Stock(env, 5m);
        Status(await Approve(env.Client, id)).ShouldBe("approved");
    }

    [Test]
    public async Task Prepayment_RequiresPercentOrAmount() {
        var env = await NewOrderEnv();

        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders", OrderBody(env, null, new { requiresPrepayment = true }));

        await Fail(response, HttpStatusCode.BadRequest, "prepayment_required");
    }
}
