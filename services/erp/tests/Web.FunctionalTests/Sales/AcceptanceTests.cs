using System.Text.Json;
using ERP.Domain.Purchasing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Purchasing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

/// <summary>Kullanıcı kabul senaryosu (spec §5): halı, metrekare fiyat, taksit planı, siparişe göre tedarik, uçtan uca.</summary>
public class AcceptanceTests : TestBase {
    private static string[] Labels(JsonElement timeline) => timeline.EnumerateArray().Select(s => s.GetProperty("label").GetString()!).ToArray();

    private static string[] States(JsonElement timeline) => timeline.EnumerateArray().Select(s => s.GetProperty("state").GetString()!).ToArray();

    [Test]
    public async Task Acceptance_HaliMetrekareTaksitBackToBack() {
        var env = await NewOrderEnv([.. OrderPermissions, "ayarlar.fiyat"], [BackToBack]);
        var today = Today();

        // P1 ürün (metrekare ile satılır, varsayılan tedarikçi) + P2 fiyat listesi (2.000 TL/m2, KDV dahil)
        var hali = await PricingApi.CreateProduct(env.Client, "HALI", new {
            vatRate = 20, baseUnit = "m2", soldByMeasure = true, measureUnit = "m2",
            suppliers = new[] { new { currentAccountId = env.Supplier, supplierCode = "YK", isDefault = true } }
        });
        var list = await PricingApi.CreateList(env.Client, "Perakende");
        (await PricingApi.PutRows(env.Client, list, PricingApi.Row(hali, PricingApi.Tiers((1, 2000m)), unit: "m2"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await PricingApi.SetSettings(env.Client, 0, new { defaultSaleListId = list });

        // P7 sipariş: 1,60 x 2,30 = 3,68 m2 -> 7.360,00; peşinat + 2 taksit
        var id = await NewOrder(env, [QLine(hali, 0m, null, new { width = 1.6m, length = 2.3m, supplyMode = "backToBack" })]);
        var draft = await GetOrder(env.Client, id);
        draft.GetProperty("total").GetDecimal().ShouldBe(7360m);
        LineQty(draft, "quantity").ShouldBe(3.68m);
        var due1 = today.AddMonths(1);
        var due2 = today.AddMonths(2);
        var plan = await Plan(env, id, 2453.33m, (due1, 2453.33m), (due2, 2453.34m));
        plan.GetProperty("plan").GetProperty("downPayment").GetDecimal().ShouldBe(2453.33m);

        var approved = await Approve(env.Client, id);
        Status(approved).ShouldBe("approved");
        approved.GetProperty("total").GetDecimal().ShouldBe(7360m);

        // P5 peşinat tahsilatı
        var account = await PaymentSupport.Account(env, "Kasa");
        await PaymentSupport.CollectOk(env, account, 2453.33m, extra: new { isAdvance = true, salesOrderId = id });
        (await GetOrder(env.Client, id)).GetProperty("advanceTotal").GetDecimal().ShouldBe(2453.33m);

        // P6 otomatik oluşan tedarik siparişi: ölçüler ve miktar
        Guid po, poLine;
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var order = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).SingleAsync();
            order.Status.ShouldBe(PurchaseOrderStatus.Draft);
            order.SupplierId.ShouldBe(env.Supplier);
            var line = order.Lines.Single();
            line.Width.ShouldBe(1.6m);
            line.Length.ShouldBe(2.3m);
            line.Quantity.ShouldBe(3.68m);
            (po, poLine) = (order.Id, line.Id);
        }

        var line0 = (await GetOrder(env.Client, id)).GetProperty("lines")[0].GetProperty("timeline");
        Labels(line0).ShouldBe(["Tedarikçiye sipariş verildi", "Kargoda", "Geldi", "Müşteriye teslim edildi"]);
        States(line0).ShouldBe(["current", "pending", "pending", "pending"]);

        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/approve");
        States((await GetOrder(env.Client, id)).GetProperty("lines")[0].GetProperty("timeline")).ShouldBe(["done", "current", "pending", "pending"]);

        // Mal kabul: irsaliye/kargo no ile
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        await PurchasingTestSupport.Receive(env.Client, PurchasingTestSupport.ReceiptBody(env, po, warehouse, [PurchasingTestSupport.RLine(poLine, hali, 3.68m)], null,
                                                                                         new { deliveryNoteNo = "YK123456" }));
        var received = await GetOrder(env.Client, id);
        var afterReceipt = received.GetProperty("lines")[0].GetProperty("timeline");
        States(afterReceipt).ShouldBe(["done", "done", "done", "current"]);
        afterReceipt[1].GetProperty("note").GetString().ShouldBe("YK123456");
        afterReceipt[2].GetProperty("note").GetString()!.ShouldContain("YK123456");

        // Sevk -> teslim
        var shipment = await Ship(env.Client, id, LineId(received), 3.68m);
        var shipmentId = shipment.GetProperty("id").GetGuid();
        using (var deliver = await env.Client.PostAsJsonAsync($"{Api}/shipments/{shipmentId}/deliver", new { receivedBy = "Ayşe Yılmaz" })) {
            deliver.StatusCode.ShouldBe(HttpStatusCode.OK, await deliver.Content.ReadAsStringAsync());
            (await deliver.JsonAsync()).GetProperty("status").GetString().ShouldBe("delivered");
        }

        var delivered = await GetOrder(env.Client, id);
        States(delivered.GetProperty("lines")[0].GetProperty("timeline")).ShouldBe(["done", "done", "done", "done"]);
        Status(delivered).ShouldBe("shipped");

        // Sevkten fatura -> kes
        var invoiceDraft = await Draft(env.Client, shipmentId);
        var invoice = await Issue(env.Client, invoiceDraft.GetProperty("id").GetGuid());
        invoice.GetProperty("status").GetString().ShouldBe("issued");
        invoice.GetProperty("totals").GetProperty("payable").GetDecimal().ShouldBe(7360m);
        DueRows(invoice).ShouldBe([(today, 2453.33m), (due1, 2453.33m), (due2, 2453.34m)]);

        var finalOrder = await GetOrder(env.Client, id);
        finalOrder.GetProperty("invoiceState").GetString().ShouldBe("full");
        finalOrder.GetProperty("plan").GetProperty("rows").EnumerateArray().ShouldAllBe(r => r.GetProperty("remaining").GetDecimal() == 0m);

        // Cari Vade sekmesi: peşinat satırı kapandı, tam 2 açık satır
        using var dueResponse = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}/due-items");
        dueResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await dueResponse.Content.ReadAsStringAsync());
        var items = (await dueResponse.JsonAsync()).EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.Select(i => (DateOnly.Parse(i.GetProperty("dueDate").GetString()!), i.GetProperty("remaining").GetDecimal())).ShouldBe([(due1, 2453.33m), (due2, 2453.34m)]);

        // Cari bakiyesi: 7.360,00 - 2.453,33 = 4.906,67 (borç)
        using var cardResponse = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}");
        (await cardResponse.JsonAsync()).GetProperty("balance").GetDecimal().ShouldBe(4906.67m);
    }
}
