using ERP.Domain.Inventory;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class ReservationTests : TestBase {
    private const string Order = "sales-order";

    [Test]
    public async Task Reserve_ReducesAvailableNotPhysical() {
        var env = await NewEnv();
        await Receive(env, 10m);

        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(), RLine(env.Warehouse, env.Product, 4m, Guid.CreateVersion7()))));

        var row = await BalanceOf(env, env.Warehouse);
        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("reserved").GetDecimal().ShouldBe(4m);
        row.GetProperty("available").GetDecimal().ShouldBe(6m);
    }

    [Test]
    public async Task Reserve_MoreThanAvailable_Returns409() {
        var env = await NewEnv();
        await Receive(env, 10m);
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(), RLine(env.Warehouse, env.Product, 8m, Guid.CreateVersion7()))));

        using var response = await Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(), RLine(env.Warehouse, env.Product, 3m, Guid.CreateVersion7())));

        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");
        (await Reservations(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task Reserve_Exempt_RequiresModule() {
        var without = await NewEnv();
        using var denied = await Op(without.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(),
                                                                          RLine(without.Warehouse, without.Product, 5m, Guid.CreateVersion7(), exempt: true)));
        await Fail(denied, HttpStatusCode.Forbidden, "module_disabled");

        var with = await NewEnv(["siparise-gore-tedarik"]);
        await Ok(Op(with.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(),
                                                       RLine(with.Warehouse, with.Product, 5m, Guid.CreateVersion7(), exempt: true))));
        (await Reservations(with.Tenant)).Single().Quantity.ShouldBe(5m);
    }

    [Test]
    public async Task Issue_CannotEatReservedStock() {
        var env = await NewEnv();
        await Receive(env, 10m);
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(), RLine(env.Warehouse, env.Product, 8m, Guid.CreateVersion7()))));

        using var rejected = await Issue(env, 5m, D3);
        await Fail(rejected, HttpStatusCode.Conflict, "insufficient_stock");

        await Ok(Issue(env, 2m, D3));
    }

    [Test]
    public async Task Release_FreesAvailable() {
        var env      = await NewEnv();
        var sourceId = Guid.CreateVersion7();
        await Receive(env, 10m);
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, sourceId, RLine(env.Warehouse, env.Product, 8m, Guid.CreateVersion7()))));

        await Ok(Op(env.Client, "release", new { sourceType = Order, sourceId, sourceLineId = (Guid?)null }));

        (await BalanceOf(env, env.Warehouse)).GetProperty("available").GetDecimal().ShouldBe(10m);
        (await Reservations(env.Tenant)).Single().Status.ShouldBe(ReservationStatus.Released);
    }

    [Test]
    public async Task Consume_TurnsReservationIntoIssue() {
        var env        = await NewEnv();
        var orderId    = Guid.CreateVersion7();
        var lineId     = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        await Receive(env, 10m, 5m);
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, orderId, RLine(env.Warehouse, env.Product, 4m, lineId))));

        await Ok(Op(env.Client, "consume", new {
            sourceType = Order, sourceId = orderId, sourceLineId = lineId, baseQuantity = 3m, movementSourceType = "shipment",
            movementSourceId = shipmentId, date = "2026-03-04"
        }));

        var row = await BalanceOf(env, env.Warehouse);
        row.GetProperty("physical").GetDecimal().ShouldBe(7m);
        row.GetProperty("reserved").GetDecimal().ShouldBe(1m);
        row.GetProperty("available").GetDecimal().ShouldBe(6m);
        var issue = (await Movements(env.Tenant)).Last();
        issue.Type.ShouldBe(MovementType.SalesShipment);
        issue.SourceId.ShouldBe(shipmentId);
        issue.Quantity.ShouldBe(-3m);
        var reservations = await Reservations(env.Tenant);
        reservations.Single(r => r.Status == ReservationStatus.Consumed).Quantity.ShouldBe(3m);
        reservations.Single(r => r.Status == ReservationStatus.Active).Quantity.ShouldBe(1m);
    }

    [Test]
    public async Task RestoreConsumed_ReactivatesReservation() {
        var env        = await NewEnv();
        var orderId    = Guid.CreateVersion7();
        var lineId     = Guid.CreateVersion7();
        var shipmentId = Guid.CreateVersion7();
        await Receive(env, 10m);
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, orderId, RLine(env.Warehouse, env.Product, 4m, lineId))));
        await Ok(Op(env.Client, "consume", new {
            sourceType = Order, sourceId = orderId, sourceLineId = lineId, baseQuantity = 4m, movementSourceType = "shipment",
            movementSourceId = shipmentId, date = "2026-03-04"
        }));

        await Ok(Op(env.Client, "reverse", new { sourceType = "shipment", sourceId = shipmentId, date = "2026-03-05" }));
        await Ok(Op(env.Client, "restore", new { sourceType = Order, sourceId = orderId, sourceLineId = lineId, baseQuantity = 4m }));

        var row = await BalanceOf(env, env.Warehouse);
        row.GetProperty("physical").GetDecimal().ShouldBe(10m);
        row.GetProperty("reserved").GetDecimal().ShouldBe(4m);
        (await Reservations(env.Tenant)).Single().Status.ShouldBe(ReservationStatus.Active);
    }

    [Test]
    public async Task RetargetExpected_MovesReservationsToReceivingWarehouse() {
        var env      = await NewEnv(["siparise-gore-tedarik"]);
        var receiver = await CreateWarehouse(env.Client, env.Branch, "ALC");
        var expected = Guid.CreateVersion7();
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(),
                                                      RLine(env.Warehouse, env.Product, 5m, Guid.CreateVersion7(), true, expected))));
        await Ok(Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(),
                                                      RLine(env.Warehouse, env.Product, 3m, Guid.CreateVersion7(), true, expected))));
        await Receive(env, 6m, warehouse: receiver);

        await Ok(Op(env.Client, "retarget", new { expectedReceiptSourceId = expected, warehouseId = receiver, baseQuantity = 6m }));

        var rows = await Reservations(env.Tenant);
        rows.Where(r => r.WarehouseId == receiver).Sum(r => r.Quantity).ShouldBe(6m);
        rows.Where(r => r.WarehouseId == receiver).All(r => r.ExpectedReceiptSourceId == null).ShouldBeTrue();
        var rest = rows.Single(r => r.WarehouseId == env.Warehouse);
        rest.Quantity.ShouldBe(2m);
        rest.ExpectedReceiptSourceId.ShouldBe(expected);
        var moved = await BalanceOf(env, receiver);
        moved.GetProperty("reserved").GetDecimal().ShouldBe(6m);
        moved.GetProperty("available").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Reserve_Bundle_Returns409() {
        var env = await NewEnv();
        var set = (await CreateOk(env.Client, new Dictionary<string, object?> {
            ["kind"] = "product", ["name"] = "Set", ["code"] = "SET", ["isBundle"] = true,
            ["bundleComponents"] = new[] { new { productId = env.Product, variantId = (Guid?)null, quantity = 1m } }
        })).GetProperty("id").GetGuid();

        using var response = await Op(env.Client, "reserve", ReserveDoc(Order, Guid.CreateVersion7(), RLine(env.Warehouse, set, 1m, Guid.CreateVersion7())));

        await Fail(response, HttpStatusCode.Conflict, "bundle_not_reservable");
    }
}
