using System.Text.Json;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

public class PlanTests : TestBase {
    private static readonly string[] PlanModules = ["teslimat-plani"];

    private const string Plans = "/api/erp/shipping/plans";

    private static Task<Env> PlanEnv() => NewOrderEnv(modules: PlanModules);

    /// <summary>Verilen müşteri için paketlenmiş sevk; carrier verilirse çıkarılır (varsayılan: Kendi aracımız).</summary>
    private static async Task<Guid> Shipment(Env env, Guid? customer = null, string? carrier = "kendi-arac", decimal quantity = 2m) {
        var (order, approved) = await ApprovedOrder(env, quantity, extra: customer is { } c ? new { currentAccountId = c } : null);
        var (shipment, _) = await DispatchSupport.PackedFor(env, order, LineId(approved), quantity);
        if (carrier is not null) await DispatchSupport.ShipOk(env, shipment, new { carrierCode = carrier, trackingNo = carrier == "kendi-arac" ? null : "TRK-1" });
        return shipment;
    }

    private static Task<HttpResponseMessage> CreatePlan(Env env, IEnumerable<Guid> shipments, object? extra = null, HttpClient? client = null) {
        var body = new Dictionary<string, object?> { ["date"] = Today().ToString("yyyy-MM-dd"), ["vehicle"] = "34 TST 01", ["driverName"] = "Ali Sürücü", ["shipmentIds"] = shipments.ToArray() };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return (client ?? env.Client).PostAsJsonAsync(Plans, body);
    }

    private static async Task<JsonElement> CreatePlanOk(Env env, params Guid[] shipments) {
        using var response = await CreatePlan(env, shipments);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    [Test]
    public async Task Plan_StopMustBeShipped() {
        var env = await PlanEnv();
        var packed = await Shipment(env, carrier: null);
        var courier = await Shipment(env, carrier: "yurtici");
        var own = await Shipment(env);

        using var notShipped = await CreatePlan(env, [packed]);
        await Fail(notShipped, HttpStatusCode.Conflict, "plan_stop_not_shipped");
        using var wrongCarrier = await CreatePlan(env, [own, courier]);
        await Fail(wrongCarrier, HttpStatusCode.Conflict, "plan_stop_not_shipped");

        var plan = await CreatePlanOk(env, own);

        plan.GetProperty("number").GetString()!.ShouldMatch(@"^TP2026\d{6}$");
        plan.GetProperty("status").GetString().ShouldBe("planned");
        plan.GetProperty("vehicle").GetString().ShouldBe("34 TST 01");
        var stop = plan.GetProperty("stops").EnumerateArray().ShouldHaveSingleItem();
        stop.GetProperty("shipmentId").GetGuid().ShouldBe(own);
        stop.GetProperty("status").GetString().ShouldBe("pending");

        // Aynı sevk başka planda bekleyen durak olamaz.
        using var duplicate = await CreatePlan(env, [own]);
        await Fail(duplicate, HttpStatusCode.Conflict, "plan_stop_duplicate");
    }

    [Test]
    public async Task Plan_Driver_MemberOrFreeText() {
        var env = await PlanEnv();
        var shipment = await Shipment(env);
        var member = Guid.CreateVersion7();
        TestEnvironment.Factory.Members.Seed(env.Tenant, member, "Mehmet Şoför");

        using var stranger = await CreatePlan(env, [shipment], new { driverName = (string?)null, driverUserId = Guid.CreateVersion7() });
        await Fail(stranger, HttpStatusCode.BadRequest, "driver_invalid");

        using var response = await CreatePlan(env, [shipment], new { driverName = (string?)null, driverUserId = member });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var plan = await response.JsonAsync();
        plan.GetProperty("driverUserId").GetGuid().ShouldBe(member);
        plan.GetProperty("driverName").GetString().ShouldBe("Mehmet Şoför");
    }

    [Test]
    public async Task Plan_StopDeliverUpdatesShipment() {
        var env = await PlanEnv();
        var first = await Shipment(env);
        var second = await Shipment(env);
        var plan = await CreatePlanOk(env, first, second);
        var planId = plan.GetProperty("id").GetGuid();
        var stops = plan.GetProperty("stops");
        var stop1 = stops[0].GetProperty("id").GetGuid();
        var stop2 = stops[1].GetProperty("id").GetGuid();

        using var invalid = await env.Client.PostAsJsonAsync($"{Plans}/{planId}/stops/{stop1}/deliver", new { receivedBy = "A" });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await invalid.Content.ReadAsStringAsync());
        using var delivered = await env.Client.PostAsJsonAsync($"{Plans}/{planId}/stops/{stop1}/deliver", new { receivedBy = "Ayşe Yılmaz", note = "Kapıda" });
        delivered.StatusCode.ShouldBe(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
        var afterDeliver = await delivered.JsonAsync();
        afterDeliver.GetProperty("status").GetString().ShouldBe("inProgress");
        afterDeliver.GetProperty("stops")[0].GetProperty("status").GetString().ShouldBe("delivered");
        var shipment = await PickSupport.GetShipment(env, first);
        shipment.GetProperty("status").GetString().ShouldBe("delivered");
        shipment.GetProperty("receivedBy").GetString().ShouldBe("Ayşe Yılmaz");
        shipment.GetProperty("proof").GetProperty("note").GetString().ShouldBe("Kapıda");

        using var again = await env.Client.PostAsJsonAsync($"{Plans}/{planId}/stops/{stop1}/deliver", new { receivedBy = "Ayşe Yılmaz" });
        await Fail(again, HttpStatusCode.Conflict, "plan_stop_closed");

        using var failed = await env.Client.PostAsJsonAsync($"{Plans}/{planId}/stops/{stop2}/fail", new { reason = "Müşteri evde yok" });
        failed.StatusCode.ShouldBe(HttpStatusCode.OK, await failed.Content.ReadAsStringAsync());
        var afterFail = await failed.JsonAsync();
        afterFail.GetProperty("status").GetString().ShouldBe("completed");
        afterFail.GetProperty("stops")[1].GetProperty("status").GetString().ShouldBe("failed");
        afterFail.GetProperty("stops")[1].GetProperty("note").GetString().ShouldBe("Müşteri evde yok");
        var failedShipment = await PickSupport.GetShipment(env, second);
        failedShipment.GetProperty("status").GetString().ShouldBe("deliveryFailed");
        failedShipment.GetProperty("attempts")[0].GetProperty("failureReason").GetString().ShouldBe("Müşteri evde yok");

        // Başarısız sevk yeniden gönderilip yeni planda tekrar durak olabilir (eski durak Failed, yenisi bekleyen).
        await PickSupport.GetShipment(env, second);
        using var resend = await env.Client.PostAsJsonAsync($"/api/erp/sales/shipments/{second}/resend", new { });
        resend.StatusCode.ShouldBe(HttpStatusCode.OK, await resend.Content.ReadAsStringAsync());
        (await CreatePlanOk(env, second)).GetProperty("stops").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Plan_ModuleDisabled_Returns403() {
        var env = await NewOrderEnv();

        using var create = await CreatePlan(env, [Guid.CreateVersion7()]);
        await Fail(create, HttpStatusCode.Forbidden, "module_disabled");
        using var list = await env.Client.GetAsync(Plans);
        await Fail(list, HttpStatusCode.Forbidden, "module_disabled");
        using var stop = await env.Client.PostAsJsonAsync($"{Plans}/{Guid.CreateVersion7()}/stops/{Guid.CreateVersion7()}/deliver", new { receivedBy = "Ali Veli" });
        await Fail(stop, HttpStatusCode.Forbidden, "module_disabled");
    }

    [Test]
    public async Task Plan_RequiresPlanPermission() {
        var env = await PlanEnv();
        var shipment = await Shipment(env);
        using var limited = await TestEnvironment.Factory.ClientAsync(env.Tenant, PlanModules, null, null, ["sevkiyat.read", "sevkiyat.deliver"]);

        using var response = await CreatePlan(env, [shipment], null, limited);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Plan_Reorder_ValidatesAndPersists() {
        var env = await PlanEnv();
        var plan = await CreatePlanOk(env, await Shipment(env), await Shipment(env), await Shipment(env));
        var planId = plan.GetProperty("id").GetGuid();
        var ids = plan.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();

        using var partial = await env.Client.PutAsJsonAsync($"{Plans}/{planId}/stops", new { stopIds = ids.Take(2).ToArray() });
        await Fail(partial, HttpStatusCode.BadRequest, "stops_mismatch");
        using var foreign = await env.Client.PutAsJsonAsync($"{Plans}/{planId}/stops", new { stopIds = new[] { ids[0], ids[1], Guid.CreateVersion7() } });
        await Fail(foreign, HttpStatusCode.BadRequest, "stops_mismatch");

        using var reordered = await env.Client.PutAsJsonAsync($"{Plans}/{planId}/stops", new { stopIds = new[] { ids[2], ids[0], ids[1] } });

        reordered.StatusCode.ShouldBe(HttpStatusCode.OK, await reordered.Content.ReadAsStringAsync());
        (await reordered.JsonAsync()).GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList().ShouldBe([ids[2], ids[0], ids[1]]);
        using var fetched = await env.Client.GetAsync($"{Plans}/{planId}");
        (await fetched.JsonAsync()).GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("sort").GetInt32()).ToList().ShouldBe([1, 2, 3]);
        using var list = await env.Client.GetAsync($"{Plans}?date={Today():yyyy-MM-dd}");
        (await list.JsonAsync()).EnumerateArray().ShouldHaveSingleItem().GetProperty("stopCount").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task PlanPdf_ListsStopsInOrder() {
        var env = await PlanEnv();
        var alpha = await CreateCari(env.Client, "M-A", customer: true, supplier: false, name: "Alfa Mağaza");
        var zeta  = await CreateCari(env.Client, "M-Z", customer: true, supplier: false, name: "Zeta Market");
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            (await db.CurrentAccounts.SingleAsync(c => c.Id == zeta)).Phone = "0532 111 22 33";
            await db.SaveChangesAsync();
        }

        var plan = await CreatePlanOk(env, await Shipment(env, alpha), await Shipment(env, zeta));
        var planId = plan.GetProperty("id").GetGuid();
        var ids = plan.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToArray();
        using var reorder = await env.Client.PutAsJsonAsync($"{Plans}/{planId}/stops", new { stopIds = new[] { ids[1], ids[0] } });
        reorder.StatusCode.ShouldBe(HttpStatusCode.OK, await reorder.Content.ReadAsStringAsync());

        using var response = await env.Client.GetAsync($"{Plans}/{planId}/pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var text = ERP.Web.FunctionalTests.Invoicing.PdfTests.Text(await response.Content.ReadAsByteArrayAsync());
        text.ShouldContain(plan.GetProperty("number").GetString()!);
        text.ShouldContain("34TST01");
        text.ShouldContain("AliSürücü");
        text.ShouldContain("05321112233");
        text.IndexOf("ZetaMarket", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0);
        text.IndexOf("AlfaMağaza", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("ZetaMarket", StringComparison.Ordinal));
    }
}
