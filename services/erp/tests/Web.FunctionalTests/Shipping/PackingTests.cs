using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

public class PackingTests : TestBase {
    /// <summary>Toplaması tamamlanmış, 4 adetlik tek satırlı sevk: (env, sevk kimliği, sevk satırı).</summary>
    private static async Task<(Env Env, Guid Shipment, Guid Line)> Picked(decimal quantity = 4m) {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, quantity);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), quantity));
        var shipmentId = draft.GetProperty("id").GetGuid();
        var list = await PickSupport.StartPick(env, shipmentId);
        await PickSupport.PutLineOk(env, list, 0, quantity);
        await PickSupport.CompleteOk(env, list.GetProperty("id").GetGuid());
        return (env, shipmentId, PickSupport.LineOf(draft));
    }

    [Test]
    public async Task Pack_ComputesDesiAndTotals() {
        var (env, shipment, line) = await Picked();

        using var response = await PickSupport.Pack(env, shipment, [
            PickSupport.Parcel(2.5m, 30m, 30m, 30m, (line, 3m)),
            PickSupport.Parcel(1m, 10m, 10m, 10m, (line, 1m))
        ]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        body.GetProperty("status").GetString().ShouldBe("packed");
        var parcels = body.GetProperty("parcels");
        parcels.GetArrayLength().ShouldBe(2);
        parcels[0].GetProperty("index").GetInt32().ShouldBe(1);
        parcels[0].GetProperty("desi").GetDecimal().ShouldBe(9m);
        parcels[1].GetProperty("desi").GetDecimal().ShouldBe(0.4m);
        body.GetProperty("totalWeightKg").GetDecimal().ShouldBe(3.5m);
        body.GetProperty("totalDesi").GetDecimal().ShouldBe(9.4m);

        // Packed iken koliler yeniden yazılabilir.
        using var again = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(4m, 20m, 20m, 20m, (line, 4m))]);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var rewritten = await again.JsonAsync();
        rewritten.GetProperty("parcels").GetArrayLength().ShouldBe(1);
        rewritten.GetProperty("totalDesi").GetDecimal().ShouldBe(2.7m);
    }

    [Test]
    public async Task Pack_ContentsMustCoverShipmentLines() {
        var (env, shipment, line) = await Picked();

        using var partial = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(2m, 30m, 30m, 30m, (line, 3m))]);
        await Fail(partial, HttpStatusCode.BadRequest, "parcel_contents_mismatch");

        using var over = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(2m, 30m, 30m, 30m, (line, 5m))]);
        await Fail(over, HttpStatusCode.BadRequest, "parcel_contents_mismatch");

        using var foreign = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(2m, 30m, 30m, 30m, (Guid.CreateVersion7(), 4m))]);
        await Fail(foreign, HttpStatusCode.BadRequest, "parcel_line_invalid");

        using var none = await PickSupport.Pack(env, shipment, []);
        await Fail(none, HttpStatusCode.Conflict, "parcel_required");

        (await PickSupport.GetShipment(env, shipment)).GetProperty("status").GetString().ShouldBe("picking");
    }

    [Test]
    public async Task Pack_RequiresCompletedPick_AndPickingStatus() {
        var env = await NewOrderEnv();
        var (order, approved) = await ApprovedOrder(env, 4m);
        var draft = await PickSupport.Draft(env, order, (LineId(approved), 4m));
        var shipmentId = draft.GetProperty("id").GetGuid();
        var line = PickSupport.LineOf(draft);

        using var fromDraft = await PickSupport.Pack(env, shipmentId, [PickSupport.Parcel(1m, 10m, 10m, 10m, (line, 4m))]);
        await Fail(fromDraft, HttpStatusCode.Conflict, "invalid_shipment_transition");

        var list = await PickSupport.StartPick(env, shipmentId);
        using var early = await PickSupport.Pack(env, shipmentId, [PickSupport.Parcel(1m, 10m, 10m, 10m, (line, 4m))]);
        await Fail(early, HttpStatusCode.Conflict, "pick_incomplete");
        list.GetProperty("status").GetString().ShouldBe("open");
    }

    [Test]
    public async Task Pack_InvalidDimensions_Returns400() {
        var (env, shipment, line) = await Picked();

        using var response = await PickSupport.Pack(env, shipment, [PickSupport.Parcel(0m, 30m, 30m, 30m, (line, 4m))]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }
}
