using System.Text.Json;
using ERP.Domain.Catalog;
using ERP.Domain.Sales;
using ERP.Infrastructure.Outbox;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

/// <summary>P8 son inceleme düzeltmeleri (backend): C1, I1, I2, I4, I5, I6, I7, I8, I9.</summary>
public class FinalReviewTests : TestBase {
    private static Task<HttpResponseMessage> Act(Env env, Guid shipment, string action, object? body = null) {
        return env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/{action}", body ?? new { });
    }

    private static async Task<JsonElement> ActOk(Env env, Guid shipment, string action, object? body = null) {
        using var response = await Act(env, shipment, action, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task SetStatus(Env env, Guid shipment, ShipmentStatus status) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        var row = await db.Shipments.SingleAsync(s => s.Id == shipment);
        row.Status = status;
        await db.SaveChangesAsync();
    }

    /// <summary>Toplaması bitmiş (Picking) sevk.</summary>
    private static async Task<(Env Env, Guid Order, Guid Shipment, Guid Line, Guid List)> Picking(decimal quantity = 4m) {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, quantity);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), quantity));
        var shipment = draft.GetProperty("id").GetGuid();
        var list = await PickSupport.StartPick(env, shipment);
        await PickSupport.PutLineOk(env, list, 0, quantity);
        var listId = list.GetProperty("id").GetGuid();
        await PickSupport.CompleteOk(env, listId);
        return (env, order, shipment, PickSupport.LineOf(draft), listId);
    }

    // ---- C1 ----

    [Test]
    public async Task Invoice_FromUnshippedShipment_Rejected() {
        var (envP, _, picking, _, _) = await Picking();
        using var fromPicking = await DraftRaw(envP.Client, picking);
        await Fail(fromPicking, HttpStatusCode.Conflict, "shipment_not_invoiceable");

        var packed = await DispatchSupport.Packed(2m);
        using var fromPacked = await DraftRaw(packed.Env.Client, packed.Shipment);
        await Fail(fromPacked, HttpStatusCode.Conflict, "shipment_not_invoiceable");

        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 3m));
        using var fromDraft = await DraftRaw(env.Client, draft.GetProperty("id").GetGuid());
        await Fail(fromDraft, HttpStatusCode.Conflict, "shipment_not_invoiceable");
    }

    [Test]
    public async Task Invoice_FromReturnedShipment_RejectedAtDraftAndAtIssue() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        var invoice = await Draft(env.Client, shipment);

        // Sevk iade edilmiş (stok geri girmiş) olsa, kesim kilit altında reddedilir.
        await SetStatus(env, shipment, ShipmentStatus.Returned);
        using var issue = await env.Client.PostAsync($"{Url}/{invoice.GetProperty("id").GetGuid()}/issue", null);
        await Fail(issue, HttpStatusCode.Conflict, "shipment_not_invoiceable");
        using var again = await DraftRaw(env.Client, shipment);
        await Fail(again, HttpStatusCode.Conflict, "shipment_not_invoiceable");
    }

    [Test]
    public async Task Invoice_FromDeliveredShipment_Allowed() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);
        await ActOk(env, shipment, "deliver", new { receivedBy = "Ayşe Yılmaz" });

        var invoice = await Draft(env.Client, shipment);

        (await Issue(env.Client, invoice.GetProperty("id").GetGuid())).GetProperty("status").GetString().ShouldBe("issued");
    }

    [Test]
    public async Task CancelDraftShipment_WithInvoicedQuantity_RejectedLikeAnyCancel() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 3m));
        var shipment = draft.GetProperty("id").GetGuid();
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var line = await db.ShipmentLines.SingleAsync(l => l.ShipmentId == shipment);
            line.InvoicedQuantity = 3m;
            await db.SaveChangesAsync();
        }

        using var cancel = await Act(env, shipment, "cancel", new { reason = "Vazgeçildi" });

        await Fail(cancel, HttpStatusCode.Conflict, "shipment_invoiced");
    }

    [Test]
    public async Task PendingToInvoice_ExcludesFailedDeliveryQuantity() {
        var (env, order, shipment, _, _) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        using (var before = await env.Client.GetAsync($"{Api}/orders/pending?toInvoice=true"))
            (await before.JsonAsync()).GetProperty("total").GetInt32().ShouldBe(1);

        await ActOk(env, shipment, "fail", new { reason = "Adres yok", at = "2026-11-21T10:00:00+03:00" });

        using var after = await env.Client.GetAsync($"{Api}/orders/pending?toInvoice=true");
        (await after.JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
        order.ShouldNotBe(Guid.Empty);
    }

    // ---- I1 ----

    [Test]
    public async Task InvoicedReturn_RequiresSalesReturnPermission() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(4m);
        await DispatchSupport.ShipOk(env, shipment);
        await Issue(env.Client, (await Draft(env.Client, shipment)).GetProperty("id").GetGuid());
        await ActOk(env, shipment, "fail", new { reason = "Adres yok", at = "2026-11-21T10:00:00+03:00" });

        using var warehouse = await OtherClient(env, "sevkiyat.read", "sevkiyat.ship");
        using var denied = await warehouse.PostAsJsonAsync($"{Api}/shipments/{shipment}/return", new { reason = "Müşteri almadı" });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            (await db.SalesReturns.CountAsync()).ShouldBe(0);
            (await db.Shipments.SingleAsync(s => s.Id == shipment)).Status.ShouldBe(ShipmentStatus.DeliveryFailed);
        }

        using var allowed = await OtherClient(env, "sevkiyat.read", "sevkiyat.ship", "satis.return");
        using var ok = await allowed.PostAsJsonAsync($"{Api}/shipments/{shipment}/return", new { reason = "Müşteri almadı" });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task UninvoicedReturn_NeedsOnlyShipPermission() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);
        await ActOk(env, shipment, "fail", new { reason = "Adres yok", at = "2026-11-21T10:00:00+03:00" });

        using var warehouse = await OtherClient(env, "sevkiyat.read", "sevkiyat.ship");
        using var response = await warehouse.PostAsJsonAsync($"{Api}/shipments/{shipment}/return", new { reason = "Müşteri almadı" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // ---- I2 ----

    [Test]
    public async Task ShippingOnlyRole_CanCreateDraftListReadAndCancel() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        using var depo = await OtherClient(env, "sevkiyat.read", "sevkiyat.pick", "sevkiyat.pack", "sevkiyat.ship", "sevkiyat.deliver");

        using var create = await depo.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, [(LineId(approved), 3m)], new { draft = true }));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var id = (await create.JsonAsync()).GetProperty("id").GetGuid();

        using var list = await depo.GetAsync($"{Api}/shipments?statuses=draft");
        list.StatusCode.ShouldBe(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        using var get = await depo.GetAsync($"{Api}/shipments/{id}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK, await get.Content.ReadAsStringAsync());
        using var cancel = await depo.PostAsJsonAsync($"{Api}/shipments/{id}/cancel", new { reason = "Vazgeçildi" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task RoleWithoutAnyShipOrReadPermission_StillForbidden() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        using var other = await OtherClient(env, "sevkiyat.pick", "satis.manage");

        using var create = await other.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, [(LineId(approved), 3m)], new { draft = true }));
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var list = await other.GetAsync($"{Api}/shipments");
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task SalesShipRole_StillCreatesAndReads() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        using var sales = await OtherClient(env, "satis.read", "satis.ship");

        using var create = await sales.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, [(LineId(approved), 3m)], new { draft = true }));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        using var list = await sales.GetAsync($"{Api}/shipments");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- I4 ----

    [Test]
    public async Task Pack_ServiceLineNeedsNoParcel_AndDtoFlagsIt() {
        var env = await NewOrderEnv();
        await Stock(env, 2m);
        var service = await CreateProduct(env.Client, "HZM1", new { kind = "service", baseUnit = "adet" });
        var order = await NewOrder(env, [QLine(env.Product, 2m, 50m), QLine(service, 1m, 100m)]);
        var approved = await Approve(env.Client, order);
        var draft = await PickSupport.Draft(env, order, (LineId(approved, 0), 2m), (LineId(approved, 1), 1m));
        draft.GetProperty("lines")[0].GetProperty("isService").GetBoolean().ShouldBeFalse();
        draft.GetProperty("lines")[1].GetProperty("isService").GetBoolean().ShouldBeTrue();
        var shipment = draft.GetProperty("id").GetGuid();
        var list = await PickSupport.StartPick(env, shipment);
        list.GetProperty("lines").GetArrayLength().ShouldBe(1);
        await PickSupport.PutLineOk(env, list, 0, 2m);
        await PickSupport.CompleteOk(env, list.GetProperty("id").GetGuid());

        using var pack = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(1m, 10m, 10m, 10m, (PickSupport.LineOf(draft), 2m))]);

        pack.StatusCode.ShouldBe(HttpStatusCode.OK, await pack.Content.ReadAsStringAsync());
        (await pack.JsonAsync()).GetProperty("status").GetString().ShouldBe("packed");
    }

    // ---- I5 ----

    [Test]
    public async Task Cancel_PickingAfterListCompleted_ReleasesOpenQuantityAndOrderCancels() {
        var (env, order, shipment, _, _) = await Picking(4m);

        var cancelled = await ActOk(env, shipment, "cancel", new { reason = "Müşteri vazgeçti" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        // Açık miktar serbest: yeni taslak açılabilir; sipariş iptal edilebilir.
        var o = await GetOrder(env.Client, order);
        var again = await PickSupport.Draft(env, order, (LineId(o), 4m));
        await ActOk(env, again.GetProperty("id").GetGuid(), "cancel", new { reason = "Tekrar vazgeçildi" });
        using var cancelOrder = await env.Client.PostAsJsonAsync($"{Api}/orders/{order}/cancel", new { reason = "İptal" });
        cancelOrder.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelOrder.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Cancel_Packed_Works() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);

        var cancelled = await ActOk(env, shipment, "cancel", new { reason = "Müşteri vazgeçti" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Test]
    public async Task Unpick_Packed_ReturnsToDraftAndClearsParcels() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);

        var unpicked = await ActOk(env, shipment, "unpick");

        unpicked.GetProperty("status").GetString().ShouldBe("draft");
        unpicked.GetProperty("pickListId").ValueKind.ShouldBe(JsonValueKind.Null);
        unpicked.GetProperty("parcels").GetArrayLength().ShouldBe(0);
        var list = await PickSupport.StartPick(env, shipment);
        list.GetProperty("lines").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Unpick_Picking_ReturnsToDraft() {
        var (env, _, shipment, _, _) = await Picking(2m);

        (await ActOk(env, shipment, "unpick")).GetProperty("status").GetString().ShouldBe("draft");
    }

    [Test]
    public async Task Unpick_Shipped_Conflict() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);

        using var response = await Act(env, shipment, "unpick");

        await Fail(response, HttpStatusCode.Conflict, "invalid_shipment_transition");
    }

    [Test]
    public async Task Unpick_RequiresPickOrPackPermission() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        using var limited = await OtherClient(env, "sevkiyat.read", "sevkiyat.deliver");

        using var response = await limited.PostAsJsonAsync($"{Api}/shipments/{shipment}/unpick", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- I6 ----

    [Test]
    public async Task PickList_LinesCarryBarcodesAndTrackingMode() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            db.Barcodes.Add(new Barcode { TenantId = env.Tenant, Value = "8690000000017", Owner = BarcodeOwner.Product, OwnerId = env.Product, ProductId = env.Product });
            var unit = new ProductUnit { ProductId = env.Product, Unit = "KOLİ", Factor = 12m, Barcode = "8690000000024", TenantId = env.Tenant };
            db.ProductUnits.Add(unit);
            db.Barcodes.Add(new Barcode { TenantId = env.Tenant, Value = "8690000000024", Owner = BarcodeOwner.Unit, OwnerId = unit.Id, ProductId = env.Product });
            await db.SaveChangesAsync();
        }

        var draft = await PickSupport.Draft(env, order, (LineId(approved), 3m));
        var list = await PickSupport.StartPick(env, draft.GetProperty("id").GetGuid());

        var line = list.GetProperty("lines")[0];
        line.GetProperty("trackingMode").GetString().ShouldBe("none");
        line.GetProperty("variantCode").ValueKind.ShouldBe(JsonValueKind.Null);
        var barcodes = line.GetProperty("barcodes").EnumerateArray().ToList();
        barcodes.Count.ShouldBe(2);
        var product = barcodes.Single(b => b.GetProperty("value").GetString() == "8690000000017");
        product.GetProperty("kind").GetString().ShouldBe("product");
        product.GetProperty("unitFactor").GetDecimal().ShouldBe(1m);
        var box = barcodes.Single(b => b.GetProperty("value").GetString() == "8690000000024");
        box.GetProperty("kind").GetString().ShouldBe("unit");
        box.GetProperty("unitFactor").GetDecimal().ShouldBe(12m);
    }

    // ---- I9 ----

    [Test]
    public async Task ShipmentList_RowCarriesBoardFields_AndFiltersByStatuses() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "yurtici", trackingNo = "YK-900" });
        await DispatchSupport.Packed(1m, env);
        var third = await DispatchSupport.Packed(1m, env);
        await ActOk(env, third.Shipment, "cancel", new { reason = "x1" });

        using var response = await env.Client.GetAsync($"{Api}/shipments?statuses=packed,shipped&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var items = (await response.JsonAsync()).GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.Select(i => i.GetProperty("status").GetString()).OrderBy(s => s).ShouldBe(["packed", "shipped"]);
        var shipped = items.Single(i => i.GetProperty("status").GetString() == "shipped");
        shipped.GetProperty("carrierCode").GetString().ShouldBe("yurtici");
        shipped.GetProperty("carrierName").GetString().ShouldNotBeNullOrEmpty();
        shipped.GetProperty("trackingNo").GetString().ShouldBe("YK-900");
        shipped.GetProperty("trackingUrl").GetString()!.ShouldContain("YK-900");
        shipped.GetProperty("parcelCount").GetInt32().ShouldBe(1);
        shipped.GetProperty("lineCount").GetInt32().ShouldBe(1);
        shipped.GetProperty("items")[0].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        shipped.GetProperty("items")[0].GetProperty("description").GetString().ShouldNotBeNullOrEmpty();
        shipped.GetProperty("customerEmailMissing").GetBoolean().ShouldBeTrue(); // test carisinin e-postası yok
        shipped.GetProperty("irsaliyeNo").GetString().ShouldNotBeNullOrEmpty();
        shipped.GetProperty("shippedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);

        using var bad = await env.Client.GetAsync($"{Api}/shipments?statuses=nonsense");
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ShipmentList_DeliveredFromFilter() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.ShipOk(env, shipment);
        await ActOk(env, shipment, "deliver", new { receivedBy = "Ayşe Yılmaz" });

        using var recent = await env.Client.GetAsync($"{Api}/shipments?statuses=delivered&deliveredFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("o"))}");
        (await recent.JsonAsync()).GetProperty("total").GetInt32().ShouldBe(1);
        using var future = await env.Client.GetAsync($"{Api}/shipments?statuses=delivered&deliveredFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("o"))}");
        (await future.JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
    }

    // ---- I7 / I8 ----

    [Test]
    public async Task Publish_DoesNotRunSubscribers_WorkerDoes_WithoutStreamPublish() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        var address = $"ayri-{Guid.NewGuid():N}@example.com";
        await DispatchSupport.SetCustomerEmail(env, address);
        await DispatchSupport.ShipOk(env, shipment);

        // Abone satırı iş işleminde açılır (akıştan bağımsız); yayın yolu aboneleri çalıştırmaz.
        (await DispatchSupport.States(env)).ShouldHaveSingleItem().CompletedAt.ShouldBeNull();
        await DispatchSupport.Relay.PublishPendingAsync(CancellationToken.None);
        (await DispatchSupport.States(env)).ShouldHaveSingleItem().CompletedAt.ShouldBeNull();

        await DispatchSupport.Subscribers.RunOnceAsync(CancellationToken.None);
        (await DispatchSupport.States(env)).ShouldHaveSingleItem().CompletedAt.ShouldNotBeNull();
        (await DispatchSupport.MailTo(address, pump: false)).ShouldNotBeNull();
    }

    [Test]
    public void SubscriberBackoff_IsExponentialOverHours_AndGivesUpAfterAboutADay() {
        OutboxSubscriberWorker.Backoff(1).ShouldBe(TimeSpan.FromSeconds(30));
        OutboxSubscriberWorker.Backoff(2).ShouldBe(TimeSpan.FromMinutes(1));
        OutboxSubscriberWorker.Backoff(3).ShouldBe(TimeSpan.FromMinutes(2));
        OutboxSubscriberWorker.Backoff(100).ShouldBe(TimeSpan.FromHours(4));

        var total = TimeSpan.Zero;
        for (var attempt = 1; attempt < OutboxSubscriberWorker.MaxAttempts; attempt++) total += OutboxSubscriberWorker.Backoff(attempt);
        total.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(20));
        total.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(26));
    }

    [Test]
    public async Task SubscriberFailure_SchedulesRetryInMinutesNotSeconds_AndGivesUpAtLimit() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        var address = $"hata2-{Guid.NewGuid():N}@example.com";
        await DispatchSupport.SetCustomerEmail(env, address);
        DispatchSupport.Mailer.FailFor(address);
        try {
            await DispatchSupport.ShipOk(env, shipment);
            await DispatchSupport.Subscribers.RunOnceAsync(CancellationToken.None);
            var first = (await DispatchSupport.States(env)).ShouldHaveSingleItem();
            first.Attempts.ShouldBe(1);
            first.GaveUp.ShouldBeFalse();
            first.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(20));

            await using (var db = PricingTestSupport.Db(env.Tenant)) {
                var row = await db.OutboxSubscriberStates.SingleAsync(s => s.TenantId == env.Tenant);
                row.Attempts = OutboxSubscriberWorker.MaxAttempts - 1;
                row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }

            await DispatchSupport.Subscribers.RunOnceAsync(CancellationToken.None);
            var last = (await DispatchSupport.States(env)).ShouldHaveSingleItem();
            last.GaveUp.ShouldBeTrue();
            last.Attempts.ShouldBe(OutboxSubscriberWorker.MaxAttempts);
        } finally {
            DispatchSupport.Mailer.Recover(address);
        }
    }
}
