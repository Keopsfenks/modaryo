using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Purchasing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class ShipmentTests : TestBase {
    [Test]
    public async Task PartialShipment_SetsPartiallyShipped() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var line = LineId(order);

        var first = await Ship(env.Client, id, line, 4m);

        first.GetProperty("status").GetString().ShouldBe("shipped");
        first.GetProperty("number").GetString()!.ShouldMatch(@"^SV2026\d{6}$");
        var partial = await GetOrder(env.Client, id);
        Status(partial).ShouldBe("partiallyShipped");
        LineQty(partial, "shippedQuantity").ShouldBe(4m);
        LineQty(partial, "openQuantity").ShouldBe(6m);

        await Ship(env.Client, id, line, 6m);

        var done = await GetOrder(env.Client, id);
        Status(done).ShouldBe("shipped");
        done.GetProperty("shipments").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task ShipExceedsOpen_Returns400() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var line = LineId(order);
        await Ship(env.Client, id, line, 7m);

        using var response = await ShipRaw(env.Client, id, line, 4m);

        await Fail(response, HttpStatusCode.BadRequest, "ship_exceeds_open");
        LineQty(await GetOrder(env.Client, id), "shippedQuantity").ShouldBe(7m);
    }

    [Test]
    public async Task Shipment_ConsumesReservationAndIssuesStock() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);

        var shipment = await Ship(env.Client, id, LineId(order), 4m);

        var reservations = await Reservations(env);
        reservations.Where(r => r.Status == ReservationStatus.Consumed).Sum(r => r.Quantity).ShouldBe(4m);
        reservations.Where(r => r.Status == ReservationStatus.Active).Sum(r => r.Quantity).ShouldBe(6m);
        var issues = await Movements(env, "sales-shipment");
        var issue  = issues.ShouldHaveSingleItem();
        issue.Type.ShouldBe(MovementType.SalesShipment);
        issue.Quantity.ShouldBe(-4m);
        issue.SourceId.ShouldBe(shipment.GetProperty("id").GetGuid());
        (await Physical(env, warehouse)).ShouldBe(6m);
    }

    [Test]
    public async Task CancelShipment_RestoresReservation() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        var shipment  = await Ship(env.Client, id, LineId(order), 6m);

        // Sevkten sonra boşalan 4 birim başka siparişe rezerve edilir: serbest stok kalmaz.
        await Stock(env, 4m);
        var other = await NewOrder(env, [QLine(env.Product, 4m, 50m)]);
        Status(await Approve(env.Client, other)).ShouldBe("approved");

        await Post(env.Client, $"shipments/{shipment.GetProperty("id").GetGuid()}/cancel", new { reason = "Yanlış sevk" });

        // Sevk edilen 6 birim fiziksel olarak geri girmiştir: rezervasyon stok kontrolü olmadan yeniden aktiftir.
        var reservations = (await Reservations(env)).Where(r => r.SourceId == id).ToList();
        reservations.Where(r => r.Status == ReservationStatus.Active).Sum(r => r.Quantity).ShouldBe(10m);
        reservations.Any(r => r.Status == ReservationStatus.Consumed).ShouldBeFalse();
        (await Physical(env, warehouse)).ShouldBe(14m);
        var cancelled = await GetOrder(env.Client, id);
        Status(cancelled).ShouldBe("approved");
        LineQty(cancelled, "shippedQuantity").ShouldBe(0m);
        using var detail = await env.Client.GetAsync($"{Api}/shipments/{shipment.GetProperty("id").GetGuid()}");
        (await detail.JsonAsync()).GetProperty("status").GetString().ShouldBe("cancelled");

        // Tekrar sevk edilebilir.
        await Ship(env.Client, id, LineId(order), 10m);
        Status(await GetOrder(env.Client, id)).ShouldBe("shipped");
    }

    [Test]
    public async Task CancelInvoicedShipment_Returns409() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var shipment = await Ship(env.Client, id, LineId(order), 10m);
        var shipmentId = shipment.GetProperty("id").GetGuid();
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var line = await db.ShipmentLines.SingleAsync(l => l.ShipmentId == shipmentId);
            line.InvoicedQuantity = 10m; // faturalanmış sevk (fatura kesimi sonraki görevde de uçtan uca denenir)
            await db.SaveChangesAsync();
        }

        using var response = await env.Client.PostAsJsonAsync($"{Api}/shipments/{shipmentId}/cancel", new { reason = "Vazgeçildi" });

        await Fail(response, HttpStatusCode.Conflict, "shipment_invoiced");
        (await Movements(env, "sales-shipment")).Count.ShouldBe(1); // stok geri alınmadı
        Status(await GetOrder(env.Client, id)).ShouldBe("shipped");
    }

    [Test]
    public async Task DeliveryAddressSnapshotFromCariDefault() {
        var env = await NewOrderEnv();
        var other = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/addresses", new { title = "Depo", line = "Sanayi Sk. 5", city = "Bursa", district = "Nilüfer", isShipping = true });
        other.StatusCode.ShouldBe(HttpStatusCode.Created, await other.Content.ReadAsStringAsync());
        using var main = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/addresses",
                                                          new { title = "Merkez", line = "Bağdat Cad. 1", city = "İstanbul", isShipping = true, isDefaultShipping = true });
        main.StatusCode.ShouldBe(HttpStatusCode.Created, await main.Content.ReadAsStringAsync());
        var (id, order) = await ApprovedOrder(env, 3m);

        var shipment = await Ship(env.Client, id, LineId(order), 3m);

        var address = shipment.GetProperty("deliveryAddress");
        address.GetProperty("address").GetString().ShouldBe("Bağdat Cad. 1");
        address.GetProperty("city").GetString().ShouldBe("İstanbul");

        // Adres sonradan değişse de sevkteki kopya aynı kalır.
        var cardId = (await (await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}")).JsonAsync()).GetProperty("addresses").EnumerateArray()
                      .Single(a => a.GetProperty("title").GetString() == "Merkez").GetProperty("id").GetGuid();
        using var update = await env.Client.PutAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/addresses/{cardId}",
                                                           new { title = "Merkez", line = "Yeni Cad. 9", city = "Ankara", isShipping = true, isDefaultShipping = true });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var again = await (await env.Client.GetAsync($"{Api}/shipments/{shipment.GetProperty("id").GetGuid()}")).JsonAsync();
        again.GetProperty("deliveryAddress").GetProperty("address").GetString().ShouldBe("Bağdat Cad. 1");
    }

    [Test]
    public async Task BackToBackLine_ShipAfterReceipt() {
        var env   = await NewOrderEnv(null, [BackToBack]);
        var id    = await NewOrder(env, [QLine(env.Product, 10m, 50m, new { supplyMode = "backToBack", supplierId = env.Supplier })]);
        var order = await Approve(env.Client, id);
        var line  = LineId(order);
        var po     = (await GetOrder(env.Client, id)).GetProperty("lines")[0].GetProperty("purchaseOrderId").GetGuid();
        var poLine = await PoLineId(env, po);
        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/approve");

        // Mal kabulden önce: tedarik gelmedi.
        using var early = await ShipRaw(env.Client, id, line, 10m);
        await Fail(early, HttpStatusCode.Conflict, "supply_pending");

        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        await PurchasingTestSupport.Receive(env.Client, PurchasingTestSupport.ReceiptBody(env, po, warehouse, [PurchasingTestSupport.RLine(poLine, env.Product, 10m)], null, new { deliveryNoteNo = "IRS-1" }));

        var shipment = await Ship(env.Client, id, line, 10m);

        shipment.GetProperty("status").GetString().ShouldBe("shipped");
        Status(await GetOrder(env.Client, id)).ShouldBe("shipped");
        (await Physical(env, warehouse)).ShouldBe(0m);
        var timeline = (await GetOrder(env.Client, id)).GetProperty("lines")[0].GetProperty("timeline");
        timeline[3].GetProperty("state").GetString().ShouldBe("done");
    }

    [Test]
    public async Task Deliver_MarksDeliveredWithReceiver() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 3m);
        var shipment = (await Ship(env.Client, id, LineId(order), 3m)).GetProperty("id").GetGuid();

        var delivered = await Post(env.Client, $"shipments/{shipment}/deliver", new { receivedBy = "Ali Veli" });

        delivered.GetProperty("status").GetString().ShouldBe("delivered");
        delivered.GetProperty("receivedBy").GetString().ShouldBe("Ali Veli");
        delivered.GetProperty("deliveredAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        using var again = await env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/deliver", new { receivedBy = "Ali Veli" });
        await Fail(again, HttpStatusCode.Conflict, "invalid_shipment_transition");
        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/cancel", new { reason = "Geri alınamaz" });
        await Fail(cancel, HttpStatusCode.Conflict, "shipment_not_cancellable");
    }

    [Test]
    public async Task ConcurrentShipments_OnlyOpenQuantityShips() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        var line = LineId(order);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ShipRaw(env.Client, id, line, 6m)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(3);
        LineQty(await GetOrder(env.Client, id), "shippedQuantity").ShouldBe(6m);
        (await Movements(env, "sales-shipment")).Sum(m => m.Quantity).ShouldBe(-6m);
    }

    [Test]
    public async Task ShipmentList_FiltersByOrder() {
        var env = await NewOrderEnv();
        var (id, order) = await ApprovedOrder(env, 10m);
        await Ship(env.Client, id, LineId(order), 4m);
        await Ship(env.Client, id, LineId(order), 2m);

        using var response = await env.Client.GetAsync($"{Api}/shipments?orderId={id}");

        var list = await response.JsonAsync();
        list.GetProperty("total").GetInt32().ShouldBe(2);
        list.GetProperty("items")[0].GetProperty("orderNumber").GetString().ShouldBe(order.GetProperty("number").GetString());
    }

    private static async Task<Guid> PoLineId(Env env, Guid po) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return (await db.PurchaseOrderLines.SingleAsync(l => l.PurchaseOrderId == po)).Id;
    }
}
