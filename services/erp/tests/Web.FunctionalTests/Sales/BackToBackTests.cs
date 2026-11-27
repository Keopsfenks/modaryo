using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Purchasing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class BackToBackTests : TestBase {
    private static object B2b(Guid? supplier = null) => new { supplyMode = "backToBack", supplierId = supplier };

    private static async Task<List<PurchaseOrder>> Pos(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).OrderBy(p => p.Number).ToListAsync();
    }

    /// <summary>Tek satırlı siparişe göre tedarik siparişini onaylar ve (siparişi, satın alma siparişi, satırı) döner.</summary>
    private static async Task<(Env Env, Guid Order, Guid Po, Guid PoLine)> Approved(decimal quantity = 10m) {
        var env   = await NewOrderEnv(null, [BackToBack]);
        var order = await NewOrder(env, [QLine(env.Product, quantity, 50m, B2b(env.Supplier))]);
        Status(await Approve(env.Client, order)).ShouldBe("approved");
        var po = (await Pos(env)).Single();
        return (env, order, po.Id, po.Lines.Single().Id);
    }

    [Test]
    public async Task BackToBack_CreatesDraftPoPerSupplier_ExemptFromStock() {
        var env      = await NewOrderEnv(null, [BackToBack]);
        var other    = await CreateCari(env.Client, "T2", customer: false, supplier: true);
        var second   = await PricingApi.CreateProduct(env.Client, "P2", new { vatRate = 20, baseUnit = "adet" });
        var id       = await NewOrder(env, [QLine(env.Product, 3m, 50m, B2b(env.Supplier)), QLine(second, 2m, 40m, B2b(other)), QLine(second, 1m, 40m, B2b(env.Supplier))]);

        var order = await Approve(env.Client, id); // stok yok: siparişe göre tedarik satırları stok kontrolünden muaftır

        Status(order).ShouldBe("approved");
        var pos = await Pos(env);
        pos.Count.ShouldBe(2);
        pos.ShouldAllBe(p => p.Status == PurchaseOrderStatus.Draft);
        pos.Single(p => p.SupplierId == env.Supplier).Lines.Count.ShouldBe(2);
        pos.Single(p => p.SupplierId == other).Lines.Count.ShouldBe(1);
        pos.SelectMany(p => p.Lines).ShouldAllBe(l => l.SalesOrderLineId != null);
        var reservations = await Reservations(env);
        reservations.Count.ShouldBe(3);
        reservations.ShouldAllBe(r => r.ExpectedReceiptSourceId != null && pos.Any(p => p.Id == r.ExpectedReceiptSourceId));
        order.GetProperty("lines")[0].GetProperty("purchaseOrderId").GetGuid().ShouldBe(pos.Single(p => p.SupplierId == env.Supplier).Id);
    }

    [Test]
    public async Task BackToBack_DefaultSupplierFromProductCard_AndMissingSupplier400() {
        var env      = await NewOrderEnv(null, [BackToBack]);
        var withCard = await PricingApi.CreateProduct(env.Client, "P2", new {
            vatRate = 20, baseUnit = "adet", suppliers = new[] { new { currentAccountId = env.Supplier, supplierCode = "X", isDefault = true } }
        });
        var good = await NewOrder(env, [QLine(withCard, 1m, 50m, B2b())]);
        var bad  = await NewOrder(env, [QLine(env.Product, 1m, 50m, B2b())]);

        Status(await Approve(env.Client, good)).ShouldBe("approved");
        using var response = await ApproveRaw(env.Client, bad);

        await Fail(response, HttpStatusCode.BadRequest, "supplier_required");
        (await Pos(env)).Count.ShouldBe(1);
    }

    [Test]
    public async Task BackToBack_ModuleDisabled_Returns403() {
        var env = await NewOrderEnv();
        var id  = await NewOrder(env, [QLine(env.Product, 2m, 50m, B2b(env.Supplier))]);

        using var response = await ApproveRaw(env.Client, id);

        await Fail(response, HttpStatusCode.Forbidden, "module_disabled");
        Status(await GetOrder(env.Client, id)).ShouldBe("draft");
        (await Pos(env)).ShouldBeEmpty();
        (await Reservations(env)).ShouldBeEmpty();
    }

    [Test]
    public async Task ReceiptUpdatesOrderLineTimeline() {
        var (env, orderId, po, poLine) = await Approved(10m);
        var before = (await GetOrder(env.Client, orderId)).GetProperty("lines")[0].GetProperty("timeline");
        before.GetArrayLength().ShouldBe(4);
        before[0].GetProperty("state").GetString().ShouldBe("current"); // taslak sipariş henüz tedarikçiye verilmedi

        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/approve");
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var afterApprove = (await GetOrder(env.Client, orderId)).GetProperty("lines")[0].GetProperty("timeline");
        afterApprove[0].GetProperty("state").GetString().ShouldBe("done");
        afterApprove[0].GetProperty("label").GetString().ShouldBe("Tedarikçiye sipariş verildi");

        await PurchasingTestSupport.Receive(env.Client, PurchasingTestSupport.ReceiptBody(env, po, warehouse, [PurchasingTestSupport.RLine(poLine, env.Product, 10m)], null, new { deliveryNoteNo = "IRS-77" }));

        var line     = (await GetOrder(env.Client, orderId)).GetProperty("lines")[0];
        var timeline = line.GetProperty("timeline");
        timeline[2].GetProperty("label").GetString().ShouldBe("Geldi");
        timeline[2].GetProperty("state").GetString().ShouldBe("done");
        timeline[2].GetProperty("note").GetString()!.ShouldContain("IRS-77");
        timeline[3].GetProperty("state").GetString().ShouldBe("current");
        var reservation = (await Reservations(env)).Single();
        reservation.WarehouseId.ShouldBe(warehouse);
        reservation.ExpectedReceiptSourceId.ShouldBeNull();
        reservation.Status.ShouldBe(ReservationStatus.Active);
        (await StockTestSupport.OutboxCount(env.Tenant, "sales.supply-received")).ShouldBe(1);
    }

    [Test]
    public async Task PoClosed_ReleasesBackToBackReservation() {
        var (env, orderId, po, poLine) = await Approved(10m);
        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/approve");
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        await PurchasingTestSupport.Receive(env.Client, PurchasingTestSupport.ReceiptBody(env, po, warehouse, [PurchasingTestSupport.RLine(poLine, env.Product, 6m)]));

        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/close", new { reason = "Tedarikçi kalanı gönderemiyor" });

        var rows = await Reservations(env);
        rows.Where(r => r.WarehouseId == warehouse).Sum(r => r.Quantity).ShouldBe(6m);
        rows.Where(r => r.WarehouseId == warehouse).ShouldAllBe(r => r.Status == ReservationStatus.Active);
        var released = rows.Single(r => r.WarehouseId != warehouse);
        released.Status.ShouldBe(ReservationStatus.Released);
        released.Quantity.ShouldBe(4m);
        (await StockTestSupport.OutboxCount(env.Tenant, "sales.supply-closed")).ShouldBe(1);
        (await GetOrder(env.Client, orderId)).GetProperty("lines")[0].GetProperty("purchaseOrderId").GetGuid().ShouldBe(po);
    }

    [Test]
    public async Task PoCancelled_UnlinksSalesLineAndReleases() {
        var (env, orderId, po, _) = await Approved(10m);

        await PurchasingTestSupport.Post(env.Client, $"orders/{po}/cancel", new { reason = "Tedarikçi iptal etti" });

        var line = (await GetOrder(env.Client, orderId)).GetProperty("lines")[0];
        IsNullOrMissing(line, "purchaseOrderId").ShouldBeTrue();
        line.GetProperty("timeline")[0].GetProperty("key").GetString().ShouldBe("supply-cancelled");
        (await Reservations(env)).ShouldAllBe(r => r.Status == ReservationStatus.Released);
        (await StockTestSupport.OutboxCount(env.Tenant, "sales.supply-cancelled")).ShouldBe(1);
        Status(await GetOrder(env.Client, orderId)).ShouldBe("approved");
    }

    [Test]
    public async Task Cancel_CancelsDraftPoAndReleases() {
        var (env, orderId, po, _) = await Approved(10m);

        await Post(env.Client, $"orders/{orderId}/cancel", new { reason = "Müşteri vazgeçti" });

        (await Pos(env)).Single(p => p.Id == po).Status.ShouldBe(PurchaseOrderStatus.Cancelled);
        (await Reservations(env)).ShouldAllBe(r => r.Status == ReservationStatus.Released);
    }

    [Test]
    public async Task MeasureLineDimensionsFlowToPo() {
        var env  = await NewOrderEnv(null, [BackToBack]);
        var hali = await PricingApi.CreateProduct(env.Client, "HALI1", new { vatRate = 20, baseUnit = "m2", soldByMeasure = true, measureUnit = "m2" });
        var id   = await NewOrder(env, [QLine(hali, 0m, 2000m, new { vatIncluded = true, width = 1.6m, length = 2.3m, supplyMode = "backToBack", supplierId = env.Supplier })]);

        var order = await Approve(env.Client, id);

        order.GetProperty("total").GetDecimal().ShouldBe(7360m);
        var line = (await Pos(env)).Single().Lines.Single();
        line.Width.ShouldBe(1.6m);
        line.Length.ShouldBe(2.3m);
        line.Quantity.ShouldBe(3.68m);
        (await Reservations(env)).Single().Quantity.ShouldBe(3.68m);
    }
}
