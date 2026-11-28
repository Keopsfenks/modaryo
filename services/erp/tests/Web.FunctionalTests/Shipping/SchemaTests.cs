using ERP.Domain.Sales;
using ERP.Domain.Shipping;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Shipping;

public class SchemaTests : TestBase {
    [Test]
    public async Task Migration_CreatesShippingTables() {
        await using var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        var             tables  = new List<string>();
        await using var reader  = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        foreach (var t in new[] { "pick_lists", "pick_lines", "parcels", "delivery_proofs", "delivery_attempts", "delivery_plans", "delivery_stops" }) tables.ShouldContain(t);
    }

    [Test]
    public async Task ShippingGraph_RoundTrips() {
        var env = await NewSalesEnv();
        var (orderId, order) = await ApprovedOrder(env, 4m);
        var shipment = await Ship(env.Client, orderId, LineId(order), 4m);
        var shipmentId = shipment.GetProperty("id").GetGuid();
        var lineId = shipment.GetProperty("lines")[0].GetProperty("id").GetGuid();

        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var s = await db.Shipments.SingleAsync(x => x.Id == shipmentId);
            s.CarrierCode = "yurtici";
            s.TrackingNo  = "123456";
            s.IrsaliyeNo  = "IR2026000001";
            s.DispatchAt  = DateTimeOffset.UtcNow;
            s.Vehicle     = "34 ABC 123";
            s.Driver      = "Ali";
            db.Parcels.Add(new Parcel { ShipmentId = shipmentId, Index = 1, WeightKg = 2.5m, WidthCm = 30, LengthCm = 30, HeightCm = 30, Desi = Desi.Of(30, 30, 30), Contents = [new ParcelContent(lineId, 4m)] });
            db.DeliveryProofs.Add(new DeliveryProof { ShipmentId = shipmentId, ReceivedBy = "Veli", DeliveredAt = DateTimeOffset.UtcNow, Note = "Kapıda" });
            var pick = new PickList { Number = "TL2026000001", BranchId = s.BranchId, WarehouseId = s.WarehouseId, ShipmentIds = [shipmentId] };
            pick.Lines.Add(new PickLine {
                PickListId = pick.Id, Sort = 1, WarehouseId = s.WarehouseId, ProductId = env.Product, Description = "Kalem", Unit = "Adet", Factor = 1m, Quantity = 4m,
                SuggestedLots = [new PickLot("L1", null, 4m, null)], Sources = [new PickSource(lineId, 4m)]
            });
            db.PickLists.Add(pick);
            var plan = new DeliveryPlan { BranchId = s.BranchId, Date = DateOnly.FromDateTime(DateTime.UtcNow), Vehicle = "34 ABC 123", DriverName = "Ali" };
            plan.Stops.Add(new DeliveryStop { DeliveryPlanId = plan.Id, Sort = 1, ShipmentId = shipmentId, Address = s.DeliveryAddress, Status = StopStatus.Pending });
            db.DeliveryPlans.Add(plan);
            await db.SaveChangesAsync();
        }

        await using var read = PricingTestSupport.Db(env.Tenant);
        var loaded = await read.Shipments.Include(x => x.Parcels).Include(x => x.Attempts).SingleAsync(x => x.Id == shipmentId);
        loaded.CarrierCode.ShouldBe("yurtici");
        loaded.Parcels.ShouldHaveSingleItem().Desi.ShouldBe(9m);
        loaded.Parcels[0].Contents.ShouldHaveSingleItem().Quantity.ShouldBe(4m);
        loaded.Attempts.ShouldHaveSingleItem().AttemptNo.ShouldBe(1);
        (await read.PickLists.Include(p => p.Lines).SingleAsync()).Lines[0].Sources!.Single().Quantity.ShouldBe(4m);
        (await read.DeliveryPlans.Include(p => p.Stops).SingleAsync()).Stops.Single().Status.ShouldBe(StopStatus.Pending);
        (await read.DeliveryProofs.SingleAsync()).ReceivedBy.ShouldBe("Veli");
    }

    [Test]
    public async Task ShippingStatuses_PersistAsText() {
        var env = await NewSalesEnv();
        var (orderId, order) = await ApprovedOrder(env, 1m);
        var shipment = await Ship(env.Client, orderId, LineId(order), 1m);
        var id = shipment.GetProperty("id").GetGuid();
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.Shipments.SingleAsync(x => x.Id == id)).Status = ShipmentStatus.DeliveryFailed;
        await db.SaveChangesAsync();
        (await db.Shipments.AsNoTracking().SingleAsync(x => x.Id == id)).Status.ShouldBe(ShipmentStatus.DeliveryFailed);
    }
}
