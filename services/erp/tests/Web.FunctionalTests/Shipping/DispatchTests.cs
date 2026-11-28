using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Platform;
using ERP.Infrastructure.Outbox;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

internal static class DispatchSupport {
    public const string Mailpit = "http://localhost:8025/api/v1";

    public static readonly HttpClient Http = new();

    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static OutboxRelay Relay => TestEnvironment.Factory.Services.GetRequiredService<OutboxRelay>();

    public static OutboxSubscriberWorker Subscribers => TestEnvironment.Factory.Services.GetRequiredService<OutboxSubscriberWorker>();

    /// <summary>Yayın ve abone döngüsünü birer kez çalıştırır (arka plan hizmetleri testte kapalıdır).</summary>
    public static async Task Pump() {
        await Relay.PublishPendingAsync(CancellationToken.None);
        await Subscribers.RunOnceAsync(CancellationToken.None);
    }

    public static TestMailerGate Mailer => TestEnvironment.Factory.Services.GetRequiredService<TestMailerGate>();

    /// <summary>Toplaması ve paketlemesi bitmiş (Packed) sevk: (env, sipariş, sevk, sevk satırı, depo).</summary>
    public static async Task<(Env Env, Guid Order, Guid Shipment, Guid Line, Guid Warehouse)> Packed(decimal quantity = 4m, Env? existing = null, decimal? stock = null) {
        var env = existing ?? await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, quantity, stock: stock);
        var (shipment, line) = await PackedFor(env, order, LineId(approved), quantity);
        return (env, order, shipment, line, await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch));
    }

    public static async Task<(Guid Shipment, Guid Line)> PackedFor(Env env, Guid order, Guid orderLine, decimal quantity) {
        var draft = await PickSupport.Draft(env, order, (orderLine, quantity));
        var shipment = draft.GetProperty("id").GetGuid();
        var list = await PickSupport.StartPick(env, shipment);
        await PickSupport.PutLineOk(env, list, 0, quantity);
        await PickSupport.CompleteOk(env, list.GetProperty("id").GetGuid());
        var line = PickSupport.LineOf(draft);
        using var pack = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(1m, 10m, 10m, 10m, (line, quantity))]);
        pack.StatusCode.ShouldBe(HttpStatusCode.OK, await pack.Content.ReadAsStringAsync());
        return (shipment, line);
    }

    public static Task<HttpResponseMessage> ShipRaw(Env env, Guid shipment, object? body = null) {
        return env.Client.PostAsJsonAsync($"{Api}/shipments/{shipment}/ship", body ?? new { carrierCode = "yurtici", trackingNo = "TRK-" + shipment.ToString("N")[..8] });
    }

    public static async Task<JsonElement> ShipOk(Env env, Guid shipment, object? body = null) {
        using var response = await ShipRaw(env, shipment, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task SetCustomerEmail(Env env, string? email) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        var cari = await db.CurrentAccounts.SingleAsync(c => c.Id == env.Customer);
        cari.Email = email;
        await db.SaveChangesAsync();
    }

    public static async Task<List<OutboxSubscriberState>> States(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.OutboxSubscriberStates.AsNoTracking().Where(s => s.TenantId == env.Tenant).OrderBy(s => s.MessageId).ToListAsync();
    }

    /// <summary>Alıcıya giden son postayı Mailpit'te arar (röle çalıştırılarak, en çok ~5 sn).</summary>
    public static async Task<JsonElement?> MailTo(string address, bool pump = true) {
        for (var attempt = 0; attempt < (pump ? 25 : 2); attempt++) {
            if (pump) await Pump();
            var list = JsonDocument.Parse(await Http.GetStringAsync($"{Mailpit}/messages?limit=200")).RootElement;
            foreach (var message in list.GetProperty("messages").EnumerateArray())
                if (message.GetProperty("To").EnumerateArray().Any(t => string.Equals(t.GetProperty("Address").GetString(), address, StringComparison.OrdinalIgnoreCase)))
                    return JsonDocument.Parse(await Http.GetStringAsync($"{Mailpit}/message/{message.GetProperty("ID").GetString()}")).RootElement.Clone();

            await Task.Delay(200);
        }

        return null;
    }
}

public class DispatchTests : TestBase {
    [Test]
    public async Task Ship_AssignsIrsaliyeNumberAndIssuesStockOnce() {
        var (env, order, shipment, _, warehouse) = await DispatchSupport.Packed(4m);
        (await Physical(env, warehouse)).ShouldBe(4m);

        var shipped = await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "yurtici", trackingNo = "YK123", dispatchAt = "2026-11-20T14:35:00+03:00", vehicle = "34 ABC 123", driver = "Ali" });

        shipped.GetProperty("status").GetString().ShouldBe("shipped");
        shipped.GetProperty("irsaliyeNo").GetString()!.ShouldMatch(@"^IR2026\d{6}$");
        shipped.GetProperty("carrierCode").GetString().ShouldBe("yurtici");
        shipped.GetProperty("trackingNo").GetString().ShouldBe("YK123");
        shipped.GetProperty("trackingUrl").GetString().ShouldBe("https://www.yurticikargo.com/tr/online-servisler/gonderi-sorgula?code=YK123");
        shipped.GetProperty("vehicle").GetString().ShouldBe("34 ABC 123");
        shipped.GetProperty("attempts").GetArrayLength().ShouldBe(1);
        shipped.GetProperty("attempts")[0].GetProperty("attemptNo").GetInt32().ShouldBe(1);
        (await Physical(env, warehouse)).ShouldBe(0m);
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-4m);
        LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(4m);
        (await GetOrder(env.Client, order)).GetProperty("status").GetString().ShouldBe("shipped");

        // İkinci kez sevk edilemez; ikinci sevkte aynı şubenin irsaliye sayacı ilerler.
        using var again = await DispatchSupport.ShipRaw(env, shipment);
        await Fail(again, HttpStatusCode.Conflict, "invalid_shipment_transition");
        (await Movements(env, "sales-shipment")).Count.ShouldBe(1);

        var (_, _, second, _, _) = await DispatchSupport.Packed(2m, env);
        var secondShipped = await DispatchSupport.ShipOk(env, second);
        secondShipped.GetProperty("irsaliyeNo").GetString().ShouldBe(shipped.GetProperty("irsaliyeNo").GetString()![..^6] + "000002");
    }

    [Test]
    public async Task Ship_CarrierRequiresTracking() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);

        using var none = await DispatchSupport.ShipRaw(env, shipment, new { carrierCode = "aras" });
        await Fail(none, HttpStatusCode.Conflict, "tracking_required");
        using var blank = await DispatchSupport.ShipRaw(env, shipment, new { carrierCode = "aras", trackingNo = "  " });
        await Fail(blank, HttpStatusCode.Conflict, "tracking_required");
        using var unknown = await DispatchSupport.ShipRaw(env, shipment, new { carrierCode = "yok", trackingNo = "1" });
        await Fail(unknown, HttpStatusCode.BadRequest, "carrier_invalid");
        using var missing = await DispatchSupport.ShipRaw(env, shipment, new { trackingNo = "1" });
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await missing.Content.ReadAsStringAsync());
        (await PickSupport.GetShipment(env, shipment)).GetProperty("status").GetString().ShouldBe("packed");

        // Kendi aracımız: takip numarası istenmez.
        var own = await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "kendi-arac" });
        own.GetProperty("status").GetString().ShouldBe("shipped");
        own.GetProperty("trackingNo").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Ship_FromDraftOnlyWhenPickPackOff() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 3m);
        var draft = (await PickSupport.Draft(env, order, (LineId(approved), 3m))).GetProperty("id").GetGuid();

        using var blocked = await DispatchSupport.ShipRaw(env, draft);
        await Fail(blocked, HttpStatusCode.Conflict, "invalid_shipment_transition");

        using (var settings = await env.Client.PutAsJsonAsync("/api/erp/settings/sevkiyat", new { version = 0, value = new { usePickPack = false } }))
            settings.StatusCode.ShouldBe(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var shipped = await DispatchSupport.ShipOk(env, draft);

        shipped.GetProperty("status").GetString().ShouldBe("shipped");
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-3m);
    }

    [Test]
    public async Task IrsaliyePdf_HasLinesLotsAndDispatchTime() {
        var env = await NewOrderEnv(modules: ["seri-lot"]);
        var lotProduct = await CreateProduct(env.Client, "LOT-IR", new { vatRate = 20, baseUnit = "adet", tracksSerialLot = true, trackingMode = "lot" });
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "receive", StockTestSupport.Doc("Receipt", StockTestSupport.D1, new {
            warehouseId = warehouse, productId = lotProduct, variantId = (Guid?)null, baseQuantity = 10m, unitCost = (decimal?)10m,
            lots = new[] { new { lotNo = "PARTI-77", expiry = "2027-01-01", quantity = 10m, lotId = (Guid?)null } }, serials = (string[]?)null, sourceLineId = (Guid?)null, exemptFromStockCheck = false
        })));
        var order = await NewOrder(env, [QLine(lotProduct, 6m, 50m)]);
        var approved = await Approve(env.Client, order);
        var (shipment, _) = await DispatchSupport.PackedFor(env, order, LineId(approved), 6m);
        var shipped = await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "kendi-arac", dispatchAt = "2026-11-20T14:35:00+03:00", vehicle = "34 ABC 123", driver = "Sürücü Veli" });

        using var response = await env.Client.GetAsync($"{Api}/shipments/{shipment}/irsaliye.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var text = ERP.Web.FunctionalTests.Invoicing.PdfTests.Text(await response.Content.ReadAsByteArrayAsync());
        text.ShouldContain(shipped.GetProperty("irsaliyeNo").GetString()!);
        text.ShouldContain("PARTI-77");
        text.ShouldContain("20.11.2026");
        text.ShouldContain("14:35");
        text.ShouldContain("34ABC123");
        text.ShouldContain("SürücüVeli");
        text.ShouldContain("CariM1");

        // Henüz sevk edilmemiş sevkin irsaliyesi yok.
        var (_, _, packed, _, _) = await DispatchSupport.Packed(1m, env);
        using var early = await env.Client.GetAsync($"{Api}/shipments/{packed}/irsaliye.pdf");
        await Fail(early, HttpStatusCode.Conflict, "irsaliye_not_issued");
    }

    [Test]
    public async Task Ship_EnqueuesCustomerEmail_SentViaMailpit() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        var address = $"alici-{Guid.NewGuid():N}@example.com";
        await DispatchSupport.SetCustomerEmail(env, address);

        var shipped = await DispatchSupport.ShipOk(env, shipment, new { carrierCode = "yurtici", trackingNo = "YK-777" });

        // Gönderim ancak olay iletildikten sonra (outbox) olur: istek bitince posta henüz yoktur.
        (await DispatchSupport.MailTo(address, pump: false)).ShouldBeNull();
        var message = await DispatchSupport.MailTo(address);
        message.ShouldNotBeNull();
        message.Value.GetProperty("Subject").GetString().ShouldBe($"Siparişiniz kargoya verildi ({shipped.GetProperty("number").GetString()})");
        var body = message.Value.GetProperty("HTML").GetString()!;
        body.ShouldContain("Yurtiçi");
        body.ShouldContain("YK-777");
        body.ShouldContain("code=YK-777");
        body.ShouldContain("Cari M1");
        (await DispatchSupport.States(env)).ShouldHaveSingleItem().CompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Ship_CariWithoutEmail_NoAttempt_AndDtoWarns() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        await DispatchSupport.SetCustomerEmail(env, null);

        var shipped = await DispatchSupport.ShipOk(env, shipment);
        await DispatchSupport.Pump();

        shipped.GetProperty("customerEmailMissing").GetBoolean().ShouldBeTrue();
        var state = (await DispatchSupport.States(env)).ShouldHaveSingleItem();
        state.CompletedAt.ShouldNotBeNull();
        state.Attempts.ShouldBe(0);
    }

    [Test]
    public async Task ShipNotifyFailure_DoesNotRollback() {
        var (env, order, shipment, _, warehouse) = await DispatchSupport.Packed(2m);
        var address = $"hata-{Guid.NewGuid():N}@example.com";
        await DispatchSupport.SetCustomerEmail(env, address);
        DispatchSupport.Mailer.FailFor(address);
        try {
            var shipped = await DispatchSupport.ShipOk(env, shipment);
            shipped.GetProperty("status").GetString().ShouldBe("shipped");

            await DispatchSupport.Pump();

            var failed = (await DispatchSupport.States(env)).ShouldHaveSingleItem();
            failed.Attempts.ShouldBe(1);
            failed.CompletedAt.ShouldBeNull();
            failed.LastError.ShouldNotBeNullOrWhiteSpace();
            (await PickSupport.GetShipment(env, shipment)).GetProperty("status").GetString().ShouldBe("shipped");
            (await Physical(env, warehouse)).ShouldBe(0m);
            LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(2m);

            // Posta sunucusu düzelince bekleyen deneme (geri çekilme süresi geçince) yeniden denenir.
            DispatchSupport.Mailer.Recover(address);
            await using (var db = PricingTestSupport.Db(env.Tenant)) {
                var row = await db.OutboxSubscriberStates.SingleAsync(s => s.TenantId == env.Tenant);
                row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                await db.SaveChangesAsync();
            }

            (await DispatchSupport.MailTo(address)).ShouldNotBeNull();
            var done = (await DispatchSupport.States(env)).ShouldHaveSingleItem();
            done.CompletedAt.ShouldNotBeNull();
            done.Attempts.ShouldBe(2);
        } finally {
            DispatchSupport.Mailer.Recover(address);
        }
    }

    [Test]
    public async Task ConcurrentDispatch_OnlyOneWins_StockIssuedOnce() {
        var (env, _, shipment, _, warehouse) = await DispatchSupport.Packed(5m);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => {
            using var response = await DispatchSupport.ShipRaw(env, shipment);
            return (response.StatusCode, Code: response.StatusCode == HttpStatusCode.OK ? null : await response.ErrorCodeAsync());
        }));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Where(r => r.StatusCode != HttpStatusCode.OK).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict && r.Code == "invalid_shipment_transition");
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-5m);
        (await Physical(env, warehouse)).ShouldBe(0m);
    }

    [Test]
    public async Task Dispatch_RechecksOpenQuantity() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 10m);
        var (shipment, _) = await DispatchSupport.PackedFor(env, order, LineId(approved), 6m);

        // P7 doğrudan sevki taslakları saymaz: 6 adet doğrudan sevk edilince açık miktar 4'e düşer.
        await Ship(env.Client, order, LineId(approved), 6m);

        using var response = await DispatchSupport.ShipRaw(env, shipment);

        await Fail(response, HttpStatusCode.BadRequest, "ship_exceeds_open");
        (await PickSupport.GetShipment(env, shipment)).GetProperty("status").GetString().ShouldBe("packed");
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-6m);
        LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(6m);
    }

    [Test]
    public async Task Dispatch_VsDirectShip_ConcurrentOnSameOrder_NeverOverships() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 10m);
        var (shipment, _) = await DispatchSupport.PackedFor(env, order, LineId(approved), 10m);

        var dispatch = DispatchSupport.ShipRaw(env, shipment);
        var direct   = ShipRaw(env.Client, order, LineId(approved), 10m);
        using var a = await dispatch;
        using var b = await direct;

        new[] { a.StatusCode, b.StatusCode }.Count(s => s is HttpStatusCode.OK or HttpStatusCode.Created).ShouldBe(1, $"{a.StatusCode} / {b.StatusCode}");
        (await Movements(env, "sales-shipment")).ShouldHaveSingleItem().Quantity.ShouldBe(-10m);
        LineQty(await GetOrder(env.Client, order), "shippedQuantity").ShouldBe(10m);
    }

    [Test]
    public async Task Ship_RequiresShipPermission() {
        var (env, _, shipment, _, _) = await DispatchSupport.Packed(2m);
        using var limited = await OtherClient(env, "sevkiyat.read", "sevkiyat.deliver");

        using var response = await limited.PostAsJsonAsync($"{Api}/shipments/{shipment}/ship", new { carrierCode = "kendi-arac" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
