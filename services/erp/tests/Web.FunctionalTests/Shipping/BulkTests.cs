using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

public class BulkTests : TestBase {
    private static Task<HttpResponseMessage> Bulk(Env env, IEnumerable<Guid> orders, Guid warehouse, HttpClient? client = null) {
        return (client ?? env.Client).PostAsJsonAsync($"{Api}/shipments/bulk", new { orderIds = orders.ToArray(), warehouseId = warehouse, date = Today().ToString("yyyy-MM-dd") });
    }

    private static async Task<JsonElement> BulkOk(Env env, IEnumerable<Guid> orders, Guid warehouse) {
        using var response = await Bulk(env, orders, warehouse);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<int> ShipmentCount(Env env, Guid order) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.Shipments.AsNoTracking().CountAsync(s => s.SalesOrderId == order);
    }

    [Test]
    public async Task BulkShip_PartialSuccessReportedPerOrder() {
        var env = await NewOrderEnv();
        var orders = new List<Guid>();
        for (var i = 0; i < 5; i++) orders.Add((await ApprovedOrder(env, 3m)).Id);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);

        // Üçüncü siparişin rezervasyonu yetersiz (ör. elle azaltılmış): taslak sevk açılamaz.
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var reservation = await db.Reservations.SingleAsync(r => r.SourceType == "sales-order" && r.SourceId == orders[2] && r.Status == ReservationStatus.Active);
            reservation.Quantity = 1m;
            await db.SaveChangesAsync();
        }

        var result = await BulkOk(env, orders, warehouse);

        result.GetProperty("created").GetInt32().ShouldBe(4);
        result.GetProperty("failed").GetInt32().ShouldBe(1);
        var rows = result.GetProperty("results");
        rows.GetArrayLength().ShouldBe(5);
        for (var i = 0; i < 5; i++) rows[i].GetProperty("orderId").GetGuid().ShouldBe(orders[i]);
        rows[2].GetProperty("shipmentId").ValueKind.ShouldBe(JsonValueKind.Null);
        rows[2].GetProperty("error").GetProperty("code").GetString().ShouldBe("reservation_short");
        foreach (var i in new[] { 0, 1, 3, 4 }) {
            rows[i].GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
            var shipment = await PickSupport.GetShipment(env, rows[i].GetProperty("shipmentId").GetGuid());
            shipment.GetProperty("status").GetString().ShouldBe("draft");
            shipment.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(3m);
        }

        // Yarım kalan hiçbir şey yok: başarısız siparişin sevki yok, belge numaraları boşluksuz.
        (await ShipmentCount(env, orders[2])).ShouldBe(0);
        var numbers = new[] { 0, 1, 3, 4 }.Select(i => rows[i].GetProperty("shipmentNumber").GetString()!).ToList();
        numbers.Select(n => int.Parse(n[^6..])).ShouldBe([1, 2, 3, 4]);
    }

    [Test]
    public async Task BulkShip_ReportsNotShippableAndNothingToShip() {
        var env = await NewOrderEnv();
        var (approved, _) = await ApprovedOrder(env, 2m);
        var unapproved = await NewOrder(env, [QLine(env.Product, 2m, 50m)]);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);

        var first = await BulkOk(env, [approved, unapproved, Guid.CreateVersion7()], warehouse);

        first.GetProperty("created").GetInt32().ShouldBe(1);
        first.GetProperty("failed").GetInt32().ShouldBe(2);
        first.GetProperty("results")[1].GetProperty("error").GetProperty("code").GetString().ShouldBe("order_not_shippable");
        first.GetProperty("results")[2].GetProperty("error").GetProperty("code").GetString().ShouldBe("not_found");

        // Açık miktar taslağa ayrıldı: ikinci toplu sevk için gönderilecek bir şey kalmadı.
        var second = await BulkOk(env, [approved], warehouse);
        second.GetProperty("created").GetInt32().ShouldBe(0);
        second.GetProperty("results")[0].GetProperty("error").GetProperty("code").GetString().ShouldBe("nothing_to_ship");
        (await ShipmentCount(env, approved)).ShouldBe(1);
    }

    [Test]
    public async Task BulkShip_RequiresShipPermission_AndOrders() {
        var env = await NewOrderEnv();
        var (order, _) = await ApprovedOrder(env, 2m);
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        using var limited = await OtherClient(env, "sevkiyat.read", "sevkiyat.pick");

        using var denied = await Bulk(env, [order], warehouse, limited);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var empty = await Bulk(env, [], warehouse);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync());
        (await ShipmentCount(env, order)).ShouldBe(0);
    }

    [Test]
    public async Task BulkDrafts_FeedBatchPick() {
        var env = await NewOrderEnv();
        var a = (await ApprovedOrder(env, 2m)).Id;
        var b = (await ApprovedOrder(env, 3m)).Id;
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        var result = await BulkOk(env, [a, b], warehouse);

        using var batch = await env.Client.PostAsJsonAsync($"{PickSupport.Shipping}/pick-lists/batch",
                                                           new { shipmentIds = result.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("shipmentId").GetGuid()).ToArray() });

        batch.StatusCode.ShouldBe(HttpStatusCode.Created, await batch.Content.ReadAsStringAsync());
        (await batch.JsonAsync()).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(5m);
    }
}
