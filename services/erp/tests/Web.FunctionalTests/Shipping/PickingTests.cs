using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

internal static class PickSupport {
    public const string Shipping = "/api/erp/shipping";

    public static async Task<JsonElement> Draft(Env env, Guid order, params (Guid Line, decimal Qty)[] lines) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, lines, new { draft = true }));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> StartPick(Env env, Guid shipment) {
        using var response = await env.Client.PostAsync($"{Api}/shipments/{shipment}/pick-list", null);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> PutLine(Env env, JsonElement list, int index, decimal picked, object? extra = null) {
        var body = new Dictionary<string, object?> { ["pickedQty"] = picked };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return env.Client.PutAsJsonAsync($"{Shipping}/pick-lists/{list.GetProperty("id").GetGuid()}/lines/{list.GetProperty("lines")[index].GetProperty("id").GetGuid()}", body);
    }

    public static async Task<JsonElement> PutLineOk(Env env, JsonElement list, int index, decimal picked, object? extra = null) {
        using var response = await PutLine(env, list, index, picked, extra);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> Complete(Env env, Guid list, bool? force = null) {
        return env.Client.PostAsJsonAsync($"{Shipping}/pick-lists/{list}/complete", new { force = force ?? false });
    }

    public static async Task<JsonElement> CompleteOk(Env env, Guid list, bool? force = null) {
        using var response = await Complete(env, list, force);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> GetShipment(Env env, Guid id) {
        using var response = await env.Client.GetAsync($"{Api}/shipments/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> Pack(Env env, Guid shipment, object[] parcels) {
        return env.Client.PutAsJsonAsync($"{Api}/shipments/{shipment}/parcels", new { parcels });
    }

    public static object Parcel(decimal weight, decimal w, decimal l, decimal h, params (Guid Line, decimal Qty)[] contents) {
        return new { weightKg = weight, widthCm = w, lengthCm = l, heightCm = h, contents = contents.Select(c => new { shipmentLineId = c.Line, qty = c.Qty }).ToArray() };
    }

    public static Guid LineOf(JsonElement shipment, int index = 0) => shipment.GetProperty("lines")[index].GetProperty("id").GetGuid();
}

public class PickingTests : TestBase {
    private static async Task<List<Reservation>> ActiveReservations(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.Reservations.AsNoTracking().Where(r => r.SourceType == "sales-order" && r.Status == ReservationStatus.Active).ToListAsync();
    }

    [Test]
    public async Task PickList_SuggestsFefoLots() {
        var env = await NewOrderEnv(modules: ["seri-lot"]);
        var lotProduct = await CreateProduct(env.Client, "LOT-1", new { vatRate = 20, baseUnit = "adet", tracksSerialLot = true, trackingMode = "lot" });
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        object LotIn(string no, decimal q, string expiry) => new { lotNo = no, expiry, quantity = q, lotId = (Guid?)null };
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "receive", StockTestSupport.Doc("Receipt", StockTestSupport.D1, new {
            warehouseId = warehouse, productId = lotProduct, variantId = (Guid?)null, baseQuantity = 10m, unitCost = (decimal?)10m,
            lots = new[] { LotIn("GEC", 5m, "2027-06-01"), LotIn("ERKEN", 5m, "2027-01-01") }, serials = (string[]?)null, sourceLineId = (Guid?)null, exemptFromStockCheck = false
        })));
        var order = await NewOrder(env, [QLine(lotProduct, 6m, 50m)]);
        var approved = await Approve(env.Client, order);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 6m));
        draft.GetProperty("status").GetString().ShouldBe("draft");

        var list = await PickSupport.StartPick(env, draft.GetProperty("id").GetGuid());

        list.GetProperty("number").GetString()!.ShouldMatch(@"^TL2026\d{6}$");
        list.GetProperty("status").GetString().ShouldBe("open");
        var line = list.GetProperty("lines")[0];
        var lots = line.GetProperty("suggestedLots").EnumerateArray().Select(l => (l.GetProperty("lotNo").GetString(), l.GetProperty("quantity").GetDecimal())).ToList();
        lots.ShouldBe([("ERKEN", 5m), ("GEC", 1m)]);
        line.GetProperty("location").GetString().ShouldNotBeNullOrWhiteSpace();
        (await PickSupport.GetShipment(env, draft.GetProperty("id").GetGuid())).GetProperty("status").GetString().ShouldBe("picking");
    }

    [Test]
    public async Task ShortPick_ReturnsDifferenceToOrder() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 10m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 10m));
        var shipmentId = draft.GetProperty("id").GetGuid();
        var list = await PickSupport.StartPick(env, shipmentId);

        await PickSupport.PutLineOk(env, list, 0, 8m);
        var done = await PickSupport.CompleteOk(env, list.GetProperty("id").GetGuid());

        done.GetProperty("status").GetString().ShouldBe("completed");
        var shipment = await PickSupport.GetShipment(env, shipmentId);
        shipment.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(8m);
        // Fark (2) siparişin açık miktarında kalır: yeni taslak 2 alır, 3 almaz; rezervasyon 10 olarak aktif kalır.
        (await ActiveReservations(env)).Sum(r => r.Quantity).ShouldBe(10m);
        using var tooMany = await env.Client.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, [(LineId(approved), 3m)], new { draft = true }));
        await Fail(tooMany, HttpStatusCode.BadRequest, "ship_exceeds_open");
        await PickSupport.Draft(env, order, (LineId(approved), 2m));
        LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(0m);
    }

    [Test]
    public async Task BatchPick_MergesByItem() {
        var env = await NewOrderEnv();
        var (orderA, a) = await ApprovedOrder(env, 4m);
        var (orderB, b) = await ApprovedOrder(env, 3m);
        var s1 = await PickSupport.Draft(env, orderA, (LineId(a), 4m));
        var s2 = await PickSupport.Draft(env, orderB, (LineId(b), 3m));

        using var response = await env.Client.PostAsJsonAsync($"{PickSupport.Shipping}/pick-lists/batch", new { shipmentIds = new[] { s1.GetProperty("id").GetGuid(), s2.GetProperty("id").GetGuid() } });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var list = await response.JsonAsync();
        list.GetProperty("lines").GetArrayLength().ShouldBe(1);
        list.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(7m);
        list.GetProperty("lines")[0].GetProperty("sources").GetArrayLength().ShouldBe(2);
        list.GetProperty("shipments").GetArrayLength().ShouldBe(2);
        var listId = list.GetProperty("id").GetGuid();
        foreach (var s in new[] { s1, s2 }) {
            var shipment = await PickSupport.GetShipment(env, s.GetProperty("id").GetGuid());
            shipment.GetProperty("status").GetString().ShouldBe("picking");
            shipment.GetProperty("pickListId").GetGuid().ShouldBe(listId);
        }

        // Toplu listede kısa toplama sevklere sırayla dağıtılır: 5 toplanırsa A=4, B=1.
        await PickSupport.PutLineOk(env, list, 0, 5m);
        await PickSupport.CompleteOk(env, listId);
        (await PickSupport.GetShipment(env, s1.GetProperty("id").GetGuid())).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(4m);
        (await PickSupport.GetShipment(env, s2.GetProperty("id").GetGuid())).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(1m);
    }

    [Test]
    public async Task CompleteWithUntouchedLine_Returns409UnlessForce() {
        var env = await NewOrderEnv();
        var second = await CreateProduct(env.Client, "P2", new { vatRate = 20, baseUnit = "adet" });
        await Stock(env, 5m);
        await Stock(env, 5m, product: second);
        var order = await NewOrder(env, [QLine(env.Product, 5m, 50m), QLine(second, 5m, 20m)]);
        var approved = await Approve(env.Client, order);
        var draft = await PickSupport.Draft(env, order, (LineId(approved, 0), 5m), (LineId(approved, 1), 5m));
        var list = await PickSupport.StartPick(env, draft.GetProperty("id").GetGuid());
        var listId = list.GetProperty("id").GetGuid();
        await PickSupport.PutLineOk(env, list, 0, 5m);

        using var blocked = await PickSupport.Complete(env, listId);
        await Fail(blocked, HttpStatusCode.Conflict, "pick_incomplete");

        var done = await PickSupport.CompleteOk(env, listId, force: true);
        done.GetProperty("status").GetString().ShouldBe("completed");
        var shipment = await PickSupport.GetShipment(env, draft.GetProperty("id").GetGuid());
        shipment.GetProperty("lines").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Picking_RequiresPickPermission() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 2m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 2m));
        var limited = await NewOrderEnv(ERP.Web.FunctionalTests.Sales.SalesTestSupport.Permissions.Where(p => p != "sevkiyat.pick").ToArray());

        using var response = await limited.Client.PostAsync($"{Api}/shipments/{draft.GetProperty("id").GetGuid()}/pick-list", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PickList_Pdf_ListsItems() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 2m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 2m));
        var list = await PickSupport.StartPick(env, draft.GetProperty("id").GetGuid());

        using var response = await env.Client.GetAsync($"{PickSupport.Shipping}/pick-lists/{list.GetProperty("id").GetGuid()}/pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
        text.ShouldContain("TOPLAMA");
        text.ShouldContain(list.GetProperty("number").GetString()!);
    }

    [Test]
    public async Task CancelPickList_ReturnsShipmentsToDraft() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 2m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 2m));
        var list = await PickSupport.StartPick(env, draft.GetProperty("id").GetGuid());

        using var response = await env.Client.PostAsync($"{PickSupport.Shipping}/pick-lists/{list.GetProperty("id").GetGuid()}/cancel", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var shipment = await PickSupport.GetShipment(env, draft.GetProperty("id").GetGuid());
        shipment.GetProperty("status").GetString().ShouldBe("draft");
        shipment.TryGetProperty("pickListId", out var id).ShouldBeTrue();
        id.ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
