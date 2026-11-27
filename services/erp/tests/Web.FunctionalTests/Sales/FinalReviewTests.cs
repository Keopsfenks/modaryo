using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

/// <summary>P7 son inceleme düzeltmeleri (I-1 .. I-7).</summary>
public class FinalReviewTests : TestBase {
    private static async Task<(Guid Order, Guid Invoice, Guid Line, Guid Warehouse, Guid Shipment)> Sold(Env env, decimal quantity) {
        var (id, order) = await ApprovedOrder(env, quantity);
        var ship        = await Ship(env.Client, id, LineId(order), quantity);
        var draft       = await Draft(env.Client, ship.GetProperty("id").GetGuid());
        var invoice     = await Issue(env.Client, draft.GetProperty("id").GetGuid());
        var line        = invoice.GetProperty("lines")[0].GetProperty("id").GetGuid();
        return (id, invoice.GetProperty("id").GetGuid(), line, await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch), ship.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> ReturnRaw(Env env, Guid invoice, Guid line, decimal quantity, Guid warehouse, HttpClient? client = null) {
        return (client ?? env.Client).PostAsJsonAsync($"{Api}/returns", new { invoiceId = invoice, warehouseKind = "normal", warehouseId = warehouse, lines = new[] { new { invoiceLineId = line, quantity } } });
    }

    // ---- I-1
    [Test]
    public async Task I1_CancelInvoice_WithActiveReturn_Returns409_ThenAllowedAfterReturnCancelled() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 10m);
        using var created = await ReturnRaw(env, sold.Invoice, sold.Line, 10m, sold.Warehouse);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var returnId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        using var blocked = await env.Client.PostAsJsonAsync($"{Url}/{sold.Invoice}/cancel", new { reason = "Hatalı fatura" });
        await Fail(blocked, HttpStatusCode.Conflict, "has_returns");

        await Post(env.Client, $"returns/{returnId}/cancel", new { reason = "Vazgeçildi" });
        using var ok = await env.Client.PostAsJsonAsync($"{Url}/{sold.Invoice}/cancel", new { reason = "Hatalı fatura" });
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent, await ok.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task I1_CancelShipment_WithActiveReturn_Returns409HasReturns() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 10m);
        using var created = await ReturnRaw(env, sold.Invoice, sold.Line, 4m, sold.Warehouse);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        using var blocked = await env.Client.PostAsJsonAsync($"{Api}/shipments/{sold.Shipment}/cancel", new { reason = "Yanlış sevk" });

        await Fail(blocked, HttpStatusCode.Conflict, "has_returns");
    }

    // ---- I-2
    [Test]
    public async Task I2_Return_OfInvoiceThatDidNotAffectStock_CreatesNoStockReceipt() {
        var env     = await NewOrderEnv();
        var invoice = await CreateAndIssue(env, Body(env, "Sales", [ProductLine(env, 4m, 50m)], new { affectsStock = false }));
        var line    = invoice.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);

        using var response = await ReturnRaw(env, invoice.GetProperty("id").GetGuid(), line, 1m, warehouse);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await Movements(env, "sales-return")).ShouldBeEmpty();
        (await Physical(env, warehouse)).ShouldBe(0m);
        (await response.JsonAsync()).GetProperty("returnInvoiceId").ValueKind.ShouldBe(JsonValueKind.String);
    }

    // ---- I-3
    [Test]
    public async Task I3_Collection_WithOtherCarisOrder_Returns400() {
        var env     = await NewOrderEnv();
        await Stock(env, 10m);
        var account = await PaymentSupport.Account(env, "Kasa");
        var other   = await CreateCari(env.Client, "M2", customer: true, supplier: false);
        var id      = await NewOrder(env, [QLine(env.Product, 2m, 50m)], new { requiresPrepayment = true, prepaymentAmount = 60m });
        Status(await Approve(env.Client, id)).ShouldBe("awaitingPayment");

        using var response = await PaymentSupport.Collect(env, account, 60m, other, extra: new { isAdvance = true, salesOrderId = id });

        await Fail(response, HttpStatusCode.BadRequest, "sales_order_mismatch");
        var order = await GetOrder(env.Client, id);
        Status(order).ShouldBe("awaitingPayment");
        order.GetProperty("advanceTotal").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task I3_CrossTaggedCollections_InParallel_NeverDeadlock() {
        var env     = await NewOrderEnv();
        await Stock(env, 10m);
        var account = await PaymentSupport.Account(env, "Kasa");
        var other   = await CreateCari(env.Client, "M2", customer: true, supplier: false);
        var a       = await NewOrder(env, [QLine(env.Product, 1m, 50m)]);
        var bBody   = OrderBody(env, [QLine(env.Product, 1m, 50m)]);
        bBody["currentAccountId"] = other;
        var b = (await CreateOrder(env, bBody)).GetProperty("id").GetGuid();

        var tasks = Enumerable.Range(0, 6).Select(i => i % 2 == 0
                                                      ? PaymentSupport.Collect(env, account, 10m, env.Customer, extra: new { isAdvance = true, salesOrderId = b })
                                                      : PaymentSupport.Collect(env, account, 10m, other, extra: new { isAdvance = true, salesOrderId = a }));
        var responses = await Task.WhenAll(tasks);

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.BadRequest);
    }

    // ---- I-4
    [Test]
    public async Task I4_Prepayment_WatcherFailure_DoesNotRollBackCollection() {
        var env     = await NewOrderEnv(null, [BackToBack]);
        var account = await PaymentSupport.Account(env, "Kasa");
        var id      = await NewOrder(env, [QLine(env.Product, 2m, 50m, new { supplyMode = "backToBack" })], new { requiresPrepayment = true, prepaymentAmount = 60m });
        Status(await Approve(env.Client, id)).ShouldBe("awaitingPayment"); // tedarikçi henüz doğrulanmadı

        await PaymentSupport.CollectOk(env, account, 60m, extra: new { isAdvance = true, salesOrderId = id });

        var order = await GetOrder(env.Client, id);
        order.GetProperty("advanceTotal").GetDecimal().ShouldBe(60m);
        Status(order).ShouldBe("awaitingPayment");
        (await PaymentSupport.Documents(env.Tenant)).ShouldHaveSingleItem();
    }

    // ---- I-5
    [Test]
    public async Task I5_CancelOrder_CancelsDraftPo_AndEmitsPoCancelledEvent() {
        var env   = await NewOrderEnv(null, [BackToBack]);
        var order = await NewOrder(env, [QLine(env.Product, 5m, 50m, new { supplyMode = "backToBack", supplierId = env.Supplier })]);
        Status(await Approve(env.Client, order)).ShouldBe("approved");

        await Post(env.Client, $"orders/{order}/cancel", new { reason = "Müşteri vazgeçti" });

        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.PurchaseOrders.AsNoTracking().SingleAsync()).Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        (await StockTestSupport.OutboxCount(env.Tenant, "purchase.order-cancelled")).ShouldBe(1);
        (await Reservations(env)).ShouldAllBe(r => r.Status == ReservationStatus.Released);
    }

    [Test]
    public async Task I5_CancelSalesOrder_AndCancelPo_InParallel_NoServerErrors() {
        for (var round = 0; round < 4; round++) {
            var env   = await NewOrderEnv(null, [BackToBack]);
            var order = await NewOrder(env, [QLine(env.Product, 5m, 50m, new { supplyMode = "backToBack", supplierId = env.Supplier })]);
            Status(await Approve(env.Client, order)).ShouldBe("approved");
            Guid po;
            await using (var db = PricingTestSupport.Db(env.Tenant)) po = (await db.PurchaseOrders.AsNoTracking().SingleAsync()).Id;

            var results = await Task.WhenAll(env.Client.PostAsJsonAsync($"{Api}/orders/{order}/cancel", new { reason = "Müşteri vazgeçti" }),
                                             env.Client.PostAsJsonAsync($"/api/erp/purchasing/orders/{po}/cancel", new { reason = "Tedarikçi iptal" }));

            foreach (var r in results) ((int)r.StatusCode).ShouldBeLessThan(500, await r.Content.ReadAsStringAsync());
            await using var check = PricingTestSupport.Db(env.Tenant);
            (await check.PurchaseOrders.AsNoTracking().SingleAsync()).Status.ShouldBe(PurchaseOrderStatus.Cancelled);
            (await check.Reservations.AsNoTracking().Where(r => r.SourceType == "sales-order").ToListAsync()).ShouldAllBe(r => r.Status == ReservationStatus.Released);
        }
    }

    // ---- I-6
    [Test]
    public async Task I6_OrderRefStatuses_AreCamelCase() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 4m);
        var line = LineId(order);
        var first = await Ship(env.Client, id, line, 2m);
        await Post(env.Client, $"shipments/{first.GetProperty("id").GetGuid()}/cancel", new { reason = "Yanlış sevk" });
        var second = await Ship(env.Client, id, line, 2m);
        await Issue(env.Client, (await Draft(env.Client, second.GetProperty("id").GetGuid())).GetProperty("id").GetGuid());

        var detail = await GetOrder(env.Client, id);

        detail.GetProperty("shipments").EnumerateArray().Select(s => s.GetProperty("status").GetString()).OrderBy(s => s).ShouldBe(["cancelled", "shipped"]);
        detail.GetProperty("invoices")[0].GetProperty("status").GetString().ShouldBe("issued");
        detail.GetProperty("reservations").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ShouldAllBe(s => s == "active" || s == "consumed" || s == "released");
        detail.GetProperty("reservations").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ShouldContain("consumed");
    }

    // ---- I-7
    [Test]
    public async Task I7_ReturnUnitCost_HiddenWithoutCostPermission() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m);
        using var created = await ReturnRaw(env, sold.Invoice, sold.Line, 2m, sold.Warehouse);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.JsonAsync();
        body.GetProperty("lines")[0].GetProperty("unitCost").GetDecimal().ShouldBe(10m);

        var limited = await OtherClient(env, "satis.read", "satis.return");
        using var detail = await limited.GetAsync($"{Api}/returns/{body.GetProperty("id").GetGuid()}");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());
        IsNullOrMissing((await detail.JsonAsync()).GetProperty("lines")[0], "unitCost").ShouldBeTrue();

        using var other = await ReturnRaw(env, sold.Invoice, sold.Line, 1m, sold.Warehouse, limited);
        other.StatusCode.ShouldBe(HttpStatusCode.Created, await other.Content.ReadAsStringAsync());
        IsNullOrMissing((await other.JsonAsync()).GetProperty("lines")[0], "unitCost").ShouldBeTrue();
    }
}
