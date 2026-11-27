using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class InvoicingTests : TestBase {
    [Test]
    public async Task InvoiceFromShipments_PricesFrozenAfterListChange() {
        var env     = await NewOrderEnv();
        var product = await PricingApi.CreateProduct(env.Client, "FIYAT1", new { vatRate = 20, baseUnit = "adet", defaultSalePrice = Money(100m) });
        await Stock(env, 5m, product);
        var id    = await NewOrder(env, [QLine(product, 2m, null)]);
        var order = await Approve(env.Client, id);
        order.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().ShouldBe(100m);
        var shipment = (await Ship(env.Client, id, LineId(order), 2m)).GetProperty("id").GetGuid();

        // Liste fiyatı sipariş verildikten sonra değişir.
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var card = await db.Products.SingleAsync(p => p.Id == product);
            card.DefaultSalePrice = new ERP.Domain.Catalog.Money4(250m, "TRY", true);
            await db.SaveChangesAsync();
        }

        var draft = await Draft(env.Client, shipment);

        draft.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().ShouldBe(100m);
        draft.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        draft.GetProperty("status").GetString().ShouldBe("draft");
        draft.GetProperty("totals").GetProperty("payable").GetDecimal().ShouldBe(200m);
        draft.GetProperty("affectsStock").GetBoolean().ShouldBeFalse();
        draft.GetProperty("sourceType").GetString().ShouldBe("sales-shipment");
    }

    [Test]
    public async Task PartialInvoices_SplitPlanRowsSequentially() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 4m, 1000m, new { vatIncluded = true });
        var day = Today().AddDays(30);
        await Plan(env, id, 0m, (day, 1000m), (day.AddDays(30), 1000m), (day.AddDays(60), 1000m), (day.AddDays(90), 1000m));
        var line = LineId(order);

        var first   = (await Ship(env.Client, id, line, 1.5m)).GetProperty("id").GetGuid();
        var second  = (await Ship(env.Client, id, line, 2.5m)).GetProperty("id").GetGuid();
        var draftA  = await Draft(env.Client, first);
        var invoiceA = await Issue(env.Client, draftA.GetProperty("id").GetGuid());
        var draftB  = await Draft(env.Client, second);
        var invoiceB = await Issue(env.Client, draftB.GetProperty("id").GetGuid());

        DueRows(invoiceA).ShouldBe([(day, 1000m), (day.AddDays(30), 500m)]);
        DueRows(invoiceB).ShouldBe([(day.AddDays(30), 500m), (day.AddDays(60), 1000m), (day.AddDays(90), 1000m)]);
        (DueRows(invoiceA).Sum(r => r.Amount) + DueRows(invoiceB).Sum(r => r.Amount)).ShouldBe(4000m);
        var plan = (await GetOrder(env.Client, id)).GetProperty("plan");
        plan.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("remaining").GetDecimal() == 0m);
        var state = await GetOrder(env.Client, id);
        state.GetProperty("invoiceState").GetString().ShouldBe("full");
        LineQty(state, "invoicedQuantity").ShouldBe(4m);
    }

    [Test]
    public async Task PartialInvoice_LeavesRemainderForLater_AndInvoiceStatePartial() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 4m, 1000m, new { vatIncluded = true });
        var day = Today().AddDays(30);
        await Plan(env, id, 0m, (day, 2000m), (day.AddDays(30), 2000m));
        var shipment = (await Ship(env.Client, id, LineId(order), 1m)).GetProperty("id").GetGuid();

        await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid());

        var state = await GetOrder(env.Client, id);
        state.GetProperty("invoiceState").GetString().ShouldBe("partial");
        var rows = state.GetProperty("plan").GetProperty("rows");
        rows[0].GetProperty("remaining").GetDecimal().ShouldBe(1000m);
        rows[1].GetProperty("remaining").GetDecimal().ShouldBe(2000m);
    }

    [Test]
    public async Task DownPayment_AllocatesToFirstDueLine() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 2m, 1000m, new { vatIncluded = true });
        var day = Today().AddDays(30);
        await Plan(env, id, 500m, (day, 750m), (day.AddDays(30), 750m));
        var account = await PaymentSupport.Account(env, "Kasa");
        await PaymentSupport.CollectOk(env, account, 500m, extra: new { isAdvance = true, salesOrderId = id });
        var shipment = (await Ship(env.Client, id, LineId(order), 2m)).GetProperty("id").GetGuid();

        var invoice = await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid());

        DueRows(invoice).ShouldBe([(Today(), 500m), (day, 750m), (day.AddDays(30), 750m)]);
        using var response = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}/due-items");
        var items = (await response.JsonAsync()).EnumerateArray().ToList();
        items.Count.ShouldBe(2); // peşinat satırı tahsilatla kapandı
        items.Select(i => i.GetProperty("remaining").GetDecimal()).ShouldBe([750m, 750m]);
        items.Select(i => DateOnly.Parse(i.GetProperty("dueDate").GetString()!)).ShouldBe([day, day.AddDays(30)]);
    }

    [Test]
    public async Task CancelInvoicedShipment_Returns409() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();
        await Draft(env.Client, shipment);

        using var response = await env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/cancel", new { reason = "Vazgeçildi" });

        await Fail(response, HttpStatusCode.Conflict, "shipment_invoiced");
        (await Movements(env, "sales-shipment")).Count.ShouldBe(1); // stok geri alınmadı
    }

    [Test]
    public async Task QuickInvoice_ShipsAndIssuesInOneTransaction() {
        var env = await NewOrderEnv();
        var (id, _) = await ApprovedOrder(env, 3m, 50m, null, null, 10m);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);

        var result = await Post(env.Client, $"orders/{id}/quick-invoice", new { });

        result.GetProperty("invoice").GetProperty("status").GetString().ShouldBe("issued");
        result.GetProperty("invoice").GetProperty("number").GetString().ShouldNotBeNullOrEmpty();
        result.GetProperty("invoiceId").GetGuid().ShouldBe(result.GetProperty("invoice").GetProperty("id").GetGuid());
        var order = await GetOrder(env.Client, id);
        Status(order).ShouldBe("shipped");
        order.GetProperty("invoiceState").GetString().ShouldBe("full");
        order.GetProperty("shipments").GetArrayLength().ShouldBe(1);
        (await Physical(env, warehouse)).ShouldBe(7m);
        (await Movements(env, "invoice")).ShouldBeEmpty(); // stok çıkışı yalnızca sevkte
        var invoice = (await Invoices(env)).Single();
        invoice.AffectsStock.ShouldBeFalse();
        invoice.Payable.ShouldBe(180m);
    }

    [Test]
    public async Task QuickInvoice_InsufficientStock_RollsBackAll() {
        var env = await NewOrderEnv();
        await StockTestSupport.SetStock(env.Client, allowNegative: true);
        var id    = await NewOrder(env, [QLine(env.Product, 5m, 50m)]);
        var order = await Approve(env.Client, id); // stok yok: eksi stok açıkken onaylanır
        Status(order).ShouldBe("approved");
        await StockTestSupport.SetStock(env.Client, allowNegative: false, version: 1);

        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/quick-invoice", new { });

        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
        var after = await GetOrder(env.Client, id);
        Status(after).ShouldBe("approved");
        after.GetProperty("shipments").GetArrayLength().ShouldBe(0);
        LineQty(after, "shippedQuantity").ShouldBe(0m);
        (await Invoices(env)).ShouldBeEmpty();
        (await Movements(env, "sales-shipment")).ShouldBeEmpty();
        (await Reservations(env)).ShouldHaveSingleItem().Status.ShouldBe(ReservationStatus.Active);
    }

    [Test]
    public async Task CancelInvoice_RevertsInvoiceStateAndPlanRows() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 2m, 1000m, new { vatIncluded = true });
        var day = Today().AddDays(30);
        await Plan(env, id, 400m, (day, 800m), (day.AddDays(30), 800m));
        var shipment = (await Ship(env.Client, id, LineId(order), 2m)).GetProperty("id").GetGuid();
        var invoice  = (await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid())).GetProperty("id").GetGuid();
        var issued = await GetOrder(env.Client, id);
        issued.GetProperty("invoiceState").GetString().ShouldBe("full");
        issued.GetProperty("plan").GetProperty("downPaymentRemaining").GetDecimal().ShouldBe(0m);

        using var cancel = await env.Client.PostAsJsonAsync($"{Url}/{invoice}/cancel", new { reason = "Hatalı fatura" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        var reverted = await GetOrder(env.Client, id);
        reverted.GetProperty("invoiceState").GetString().ShouldBe("none");
        LineQty(reverted, "invoicedQuantity").ShouldBe(0m);
        var plan = reverted.GetProperty("plan");
        plan.GetProperty("downPaymentRemaining").GetDecimal().ShouldBe(400m);
        plan.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("remaining").GetDecimal() == 800m);
        (await Invoices(env)).Single().Status.ShouldBe(InvoiceStatus.Cancelled);
        await using (var db = PricingTestSupport.Db(env.Tenant)) (await db.InvoicePlanConsumptions.CountAsync()).ShouldBe(0);

        // Sevk tekrar faturalanabilir, plan baştan tüketilir.
        var again = await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid());
        DueRows(again).ShouldBe([(Today(), 400m), (day, 800m), (day.AddDays(30), 800m)]);
    }

    [Test]
    public async Task ConcurrentDraftCreation_InvoicesShipmentOnlyOnce() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DraftRaw(env.Client, shipment)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await Invoices(env)).SelectMany(i => i.Lines).Sum(l => l.Quantity).ShouldBe(10m);
    }

    [Test]
    public async Task ConcurrentDraftEdits_ExactlyOneFits() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();
        var first    = await PutDraftOk(env, await Draft(env.Client, shipment), 3m);
        var second   = await PutDraftOk(env, await Draft(env.Client, shipment), 3m); // toplam 6, 4 boşta

        // Kilitsiz: ikisi de diğerini 3 görür (6 + 3 = 9 sığar), toplam 12 olurdu.
        var responses = await Task.WhenAll(PutDraft(env, first, 6m), PutDraft(env, second, 6m));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        (await Invoices(env)).SelectMany(i => i.Lines).Sum(l => l.Quantity).ShouldBeLessThanOrEqualTo(10m);
    }

    [Test]
    public async Task Issue_RechecksOverInvoicedUnderLock() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();
        await PutDraftOk(env, await Draft(env.Client, shipment), 3m);
        var secondId = (await PutDraftOk(env, await Draft(env.Client, shipment), 7m)).GetProperty("id").GetGuid();
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var line = await db.InvoiceLines.SingleAsync(l => l.InvoiceId == secondId);
            line.Quantity = 9m; // doğrudan veritabanında bozulmuş taslak: 3 + 9 > 10
            await db.SaveChangesAsync();
        }

        using var response = await env.Client.PostAsync($"{Url}/{secondId}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "shipment_over_invoiced");
        await using var check = PricingTestSupport.Db(env.Tenant);
        (await check.Invoices.SingleAsync(i => i.Id == secondId)).Status.ShouldBe(InvoiceStatus.Draft);
        (await check.SalesOrderLines.SumAsync(l => l.InvoicedQuantity)).ShouldBe(0m);
    }

    [Test]
    public async Task ConcurrentIssue_PlanConsumedOnce() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 2m, 1000m, new { vatIncluded = true });
        var day = Today().AddDays(30);
        await Plan(env, id, 0m, (day, 1000m), (day.AddDays(30), 1000m));
        var shipment = (await Ship(env.Client, id, LineId(order), 2m)).GetProperty("id").GetGuid();
        var draft    = (await Draft(env.Client, shipment)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => env.Client.PostAsync($"{Url}/{draft}/issue", null)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var plan = (await GetOrder(env.Client, id)).GetProperty("plan");
        plan.GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("remaining").GetDecimal() == 0m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.InvoicePlanConsumptions.SumAsync(c => c.Amount)).ShouldBe(2000m);
    }

    [Test]
    public async Task LinkedDraft_FieldsLocked() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();
        var draft    = await Draft(env.Client, shipment);

        using var stock = await PutDraft(env, draft, 10m, new { affectsStock = true, warehouseId = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch) });
        await Fail(stock, HttpStatusCode.BadRequest, "sales_invoice_affects_stock");

        var other = await CreateCari(env.Client, "M2", customer: true, supplier: false);
        using var cari = await PutDraft(env, draft, 10m, new { currentAccountId = other });
        await Fail(cari, HttpStatusCode.BadRequest, "sales_invoice_locked");

        using var foreign = await PutDraft(env, draft, 10m, new { lines = new[] { new { productId = env.Product, quantity = 1m, unitPrice = 5m, sourceLineId = Guid.NewGuid() } } });
        await Fail(foreign, HttpStatusCode.BadRequest, "sales_invoice_locked");
    }

    [Test]
    public async Task UnlinkedLine_CannotReplaceShipmentLink_NoDoubleInvoicing() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = (await Ship(env.Client, id, LineId(order), 10m)).GetProperty("id").GetGuid();
        var draft    = await Draft(env.Client, shipment);

        // Bağı kaldırıp serbest satır yazmak sevk miktarını sayımdan düşürürdü.
        using var unlink = await PutDraft(env, draft, 10m, new { lines = new[] { new { productId = env.Product, quantity = 10m, unitPrice = 50m } } });
        await Fail(unlink, HttpStatusCode.BadRequest, "sales_invoice_locked");

        // Veritabanında bağ koparılmış taslak kesilemez.
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var line = await db.InvoiceLines.SingleAsync();
            line.SourceLineId = null;
            await db.SaveChangesAsync();
        }

        using var issue = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);
        await Fail(issue, HttpStatusCode.Conflict, "sales_invoice_unlinked");
        (await Invoices(env)).Single().Status.ShouldBe(InvoiceStatus.Draft);
    }

    [Test]
    public async Task ConcurrentQuickInvoiceAndIssue_SameSeriesAndStock_NoDeadlock() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m, 50m, null, null, 30m);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        var shipment  = (await Ship(env.Client, id, LineId(order), 4m)).GetProperty("id").GetGuid();
        var draft     = (await Draft(env.Client, shipment)).GetProperty("id").GetGuid();
        var manual    = (await Create(env, Body(env, "Sales", [ProductLine(env, 2m, 50m)], new { affectsStock = true, warehouseId = warehouse }))).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(env.Client.PostAsync($"{Url}/{draft}/issue", null), env.Client.PostAsync($"{Url}/{manual}/issue", null),
                                           env.Client.PostAsJsonAsync($"{Api}/orders/{id}/quick-invoice", new { }), env.Client.PostAsync($"{Url}/{draft}/issue", null));

        responses.ShouldNotContain(r => (int)r.StatusCode >= 500);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(3);
        LineQty(await GetOrder(env.Client, id), "invoicedQuantity").ShouldBe(10m);
    }

    [Test]
    public async Task ManualSalesDraft_CannotLinkShipmentLines() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = await Ship(env.Client, id, LineId(order), 10m);
        var shipmentLine = shipment.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var manual = await Create(env, Body(env, "Sales", [ProductLine(env, 10m, 50m, new { sourceLineId = shipmentLine })]));

        manual.GetProperty("lines")[0].GetProperty("sourceLineId").ValueKind.ShouldBe(JsonValueKind.Null);
        await Draft(env.Client, shipment.GetProperty("id").GetGuid()); // elle taslak sevk miktarını tüketmez
    }

    [Test]
    public async Task InvoiceFromShipments_MixedOrders_Returns400() {
        var env = await NewOrderEnv();
        var (a, orderA) = await ApprovedOrder(env, 2m);
        var (b, orderB) = await ApprovedOrder(env, 2m);
        var s1 = (await Ship(env.Client, a, LineId(orderA), 2m)).GetProperty("id").GetGuid();
        var s2 = (await Ship(env.Client, b, LineId(orderB), 2m)).GetProperty("id").GetGuid();

        using var response = await DraftRaw(env.Client, s1, s2);

        await Fail(response, HttpStatusCode.BadRequest, "order_mismatch");
    }

    private static object Money(decimal amount) => new { amount, currency = "TRY", vatIncluded = true };
}
