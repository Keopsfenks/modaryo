using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class ReceiptTests : TestBase {
    private static async Task<(Env Env, Guid Po, Guid Line, Guid Warehouse)> Approved(decimal quantity = 10m, decimal price = 20m, string[]? modules = null) {
        var env       = await NewPurchasingEnv(null, modules);
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var po        = await CreatePo(env, PoBody(env, [PoLine(env.Product, quantity, price)]));
        var id        = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        return (env, id, line, warehouse);
    }

    [Test]
    public async Task PartialReceipt_SetsPartiallyReceived() {
        var (env, po, line, warehouse) = await Approved();

        var first       = await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 4m)]));
        var afterFirst  = await GetPo(env.Client, po);
        var second      = await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 6m)]));
        var afterSecond = await GetPo(env.Client, po);

        first.GetProperty("number").GetString()!.ShouldMatch(@"^MK2026\d{6}$");
        first.GetProperty("status").GetString().ShouldBe("posted");
        afterFirst.GetProperty("status").GetString().ShouldBe("partiallyReceived");
        afterFirst.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(4m);
        afterFirst.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal().ShouldBe(6m);
        second.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        afterSecond.GetProperty("status").GetString().ShouldBe("received");
        afterSecond.GetProperty("receipts").GetArrayLength().ShouldBe(2);
        (await Physical(env, warehouse)).ShouldBe(10m);
    }

    [Test]
    public async Task OverDelivery_BeyondTolerance_Returns409() {
        var (env, po, line, warehouse) = await Approved();

        using var zero = await PostReceipt(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10.5m)]));
        await SetPurchasingSettings(env.Client, 5m);
        using var beyond = await PostReceipt(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10.6m)]));

        await Fail(zero, HttpStatusCode.Conflict, "over_delivery");
        await Fail(beyond, HttpStatusCode.Conflict, "over_delivery");
        (await Physical(env, warehouse)).ShouldBe(0m);
        (await GetPo(env.Client, po)).GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task OverDelivery_WithinTolerance_Warns() {
        var (env, po, line, warehouse) = await Approved();
        await SetPurchasingSettings(env.Client, 5m);

        var receipt = await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10.4m)]));

        receipt.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain("over_delivery_within_tolerance");
        var order = await GetPo(env.Client, po);
        order.GetProperty("status").GetString().ShouldBe("received");
        order.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        (await Physical(env, warehouse)).ShouldBe(10.4m);
    }

    [Test]
    public async Task ReceiptCost_UsesReceiptDateRate() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var po        = await CreatePo(env, PoBody(env, [PoLine(env.Product, 100m, 10m)], new { currency = "USD", rate = 30m }));
        var id        = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line        = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receiptDate = Day.AddDays(1);
        await ERP.Web.FunctionalTests.Invoicing.PostingSupport.Override(env, "USD", receiptDate.ToString("yyyy-MM-dd"), 32m);

        var receipt = await Receive(env.Client, ReceiptBody(env, id, warehouse, [RLine(line, env.Product, 100m)], receiptDate));

        receipt.GetProperty("currency").GetString().ShouldBe("USD");
        receipt.GetProperty("rate").GetDecimal().ShouldBe(32m);
        var l = receipt.GetProperty("lines")[0];
        l.GetProperty("unitPrice").GetDecimal().ShouldBe(10m);
        l.GetProperty("unitCostTl").GetDecimal().ShouldBe(320m);
        var movement = (await StockTestSupport.Movements(env.Tenant)).Single();
        movement.Type.ShouldBe(MovementType.PurchaseReceipt);
        movement.UnitCost.ShouldBe(320m);
        movement.TotalCost.ShouldBe(32000m);
        movement.SourceLineId.ShouldBe(l.GetProperty("id").GetGuid());
    }

    [Test]
    public async Task LotProductReceipt_RequiresLots() {
        var env       = await NewPurchasingEnv(null, ["seri-lot"]);
        var lot       = await InventoryTestSupport.CreateProduct(env.Client, "LOT-1", new { tracksSerialLot = true, trackingMode = "lot", vatRate = 20, baseUnit = "adet" });
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var po        = await CreatePo(env, PoBody(env, [PoLine(lot, 10m, 5m)]));
        var id        = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();

        using var without = await PostReceipt(env.Client, ReceiptBody(env, id, warehouse, [RLine(line, lot, 10m)]));
        var ok = await Receive(env.Client, ReceiptBody(env, id, warehouse,
                                                       [RLine(line, lot, 10m, new { lots = new[] { new { lotNo = "L-1", quantity = 6m }, new { lotNo = "L-2", quantity = 4m } } })]));

        await Fail(without, HttpStatusCode.BadRequest, "lot_required");
        ok.GetProperty("status").GetString().ShouldBe("posted");
        (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(10m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.StockMovements.SelectMany(m => m.LotLines).CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task BackToBackReservation_FulfilledOnReceipt() {
        var (env, po, line, warehouse) = await Approved(10m, modules: ["siparise-gore-tedarik"]);
        var holding = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "BEK");
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                          StockTestSupport.RLine(holding, env.Product, 10m, Guid.CreateVersion7(), true, po))));

        await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 6m)]));

        var rows = await StockTestSupport.Reservations(env.Tenant);
        rows.Where(r => r.WarehouseId == warehouse).Sum(r => r.Quantity).ShouldBe(6m);
        rows.Where(r => r.WarehouseId == warehouse).ShouldAllBe(r => r.Status == ReservationStatus.Active && r.ExpectedReceiptSourceId == null);
        var rest = rows.Single(r => r.WarehouseId == holding);
        rest.Quantity.ShouldBe(4m);
        rest.ExpectedReceiptSourceId.ShouldBe(po);
        (await Available(env, warehouse)).ShouldBe(0m);
        (await StockTestSupport.OutboxCount(env.Tenant, "purchase.received")).ShouldBe(1);
    }

    [Test]
    public async Task ShortClose_ReleasesOutstandingAndBlocksFurtherReceipts() {
        var (env, po, line, warehouse) = await Approved(100m, modules: ["siparise-gore-tedarik"]);
        var holding = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "BEK");
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                          StockTestSupport.RLine(holding, env.Product, 100m, Guid.CreateVersion7(), true, po))));
        await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 60m)]));

        var       closed = await Post(env.Client, $"orders/{po}/close", new { reason = "Tedarikçi kalanı gönderemiyor" });
        using var later  = await PostReceipt(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10m)]));

        closed.GetProperty("status").GetString().ShouldBe("closed");
        closed.GetProperty("lines")[0].GetProperty("openQuantity").GetDecimal().ShouldBe(0m);
        await Fail(later, HttpStatusCode.Conflict, "receipt_exceeds_open");
        var rows = await StockTestSupport.Reservations(env.Tenant);
        rows.Where(r => r.WarehouseId == warehouse).ShouldAllBe(r => r.Status == ReservationStatus.Active);
        rows.Where(r => r.WarehouseId == warehouse).Sum(r => r.Quantity).ShouldBe(60m);
        var released = rows.Single(r => r.WarehouseId == holding);
        released.Status.ShouldBe(ReservationStatus.Released);
        released.Quantity.ShouldBe(40m);
        (await StockTestSupport.OutboxCount(env.Tenant, "purchase.order-closed")).ShouldBe(1);
        (await Physical(env, warehouse)).ShouldBe(60m);
    }

    [Test]
    public async Task CancelReceipt_ReversesStockAndOrder() {
        var (env, po, line, warehouse) = await Approved();
        var receipt = await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10m)]));
        var id      = receipt.GetProperty("id").GetGuid();

        var       cancelled = await Post(env.Client, $"receipts/{id}/cancel", new { reason = "Yanlış giriş" });
        using var again     = await env.Client.PostAsJsonAsync($"{Api}/receipts/{id}/cancel", new { reason = "Yanlış giriş" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        await Fail(again, HttpStatusCode.Conflict, "receipt_cancelled");
        var order = await GetPo(env.Client, po);
        order.GetProperty("status").GetString().ShouldBe("approved");
        order.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(0m);
        (await Physical(env, warehouse)).ShouldBe(0m);
    }

    [Test]
    public async Task CancelInvoicedReceipt_Returns409() {
        var (env, po, line, warehouse) = await Approved();
        var receipt = await Receive(env.Client, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 10m)]));
        var id      = receipt.GetProperty("id").GetGuid();
        var invoice = await Create(env, Body(env, "Purchase", [ProductLine(env, 10m, 20m)]));
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var row = await db.Invoices.FindAsync(invoice.GetProperty("id").GetGuid());
            row!.SourceType = "purchase-receipt";
            row.SourceId    = id;
            await db.SaveChangesAsync();
        }

        using var response = await env.Client.PostAsJsonAsync($"{Api}/receipts/{id}/cancel", new { reason = "Yanlış giriş" });

        await Fail(response, HttpStatusCode.Conflict, "receipt_invoiced");
        (await Physical(env, warehouse)).ShouldBe(10m);
    }

    [Test]
    public async Task ReceiverWithoutAlis_NoPricesAnywhere() {
        var (env, po, line, warehouse) = await Approved();
        var receiver = await OtherClient(env, "satinalma.read", "satinalma.receive");

        var       receipt = await Receive(receiver, ReceiptBody(env, po, warehouse, [RLine(line, env.Product, 4m)]));
        using var get     = await receiver.GetAsync($"{Api}/receipts/{receipt.GetProperty("id").GetGuid()}");
        using var list    = await receiver.GetAsync($"{Api}/receipts?purchaseOrderId={po}");
        using var orderResponse = await receiver.GetAsync($"{Api}/orders/{po}");

        var fetched = await get.JsonAsync();
        var rows    = await list.JsonAsync();
        var order   = await orderResponse.JsonAsync();
        foreach (var doc in new[] { receipt, fetched }) {
            doc.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
            var l = doc.GetProperty("lines")[0];
            l.GetProperty("unitPrice").ValueKind.ShouldBe(JsonValueKind.Null);
            l.GetProperty("unitCostTl").ValueKind.ShouldBe(JsonValueKind.Null);
            l.GetProperty("quantity").GetDecimal().ShouldBe(4m);
        }

        rows.GetProperty("items")[0].GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null);
        order.GetProperty("lines")[0].GetProperty("unitPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        // Fiyatsız alıcı yine de maliyetli stok girişi yazar (maliyet sunucuda siparişten gelir).
        (await StockTestSupport.Movements(env.Tenant)).Single().UnitCost.ShouldBe(20m);
    }

    [Test]
    public async Task StandaloneReceipt_WithoutAlis_Returns403_AndWithAlisPostsCost() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var receiver  = await OtherClient(env, "satinalma.read", "satinalma.receive");
        var body = new Dictionary<string, object?> {
            ["supplierId"] = env.Supplier, ["branchId"] = env.Branch, ["warehouseId"] = warehouse, ["date"] = Day.ToString("yyyy-MM-dd"),
            ["lines"] = new object[] { new { productId = env.Product, quantity = 3m, unitPrice = 15m } }
        };

        using var denied = await PostReceipt(receiver, body);
        var       ok     = await Receive(env.Client, body);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ok.GetProperty("lines")[0].GetProperty("unitCostTl").GetDecimal().ShouldBe(15m);
        (await Physical(env, warehouse)).ShouldBe(3m);
    }

    [Test]
    public async Task ConcurrentPartialReceipts_NeverOverReceiveBeyondTolerance() {
        for (var round = 0; round < 3; round++) {
            var (env, po, line, _) = await Approved(10m, 5m);
            var tolerance = round == 2 ? 10m : 0m;
            if (tolerance > 0m) await SetPurchasingSettings(env.Client, tolerance);
            var warehouses = new List<Guid>();
            for (var i = 0; i < 8; i++) warehouses.Add(await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "C" + i));
            var quantity = round == 1 ? 2m : 3m;

            var results = await Task.WhenAll(warehouses.Select(w => Task.Run(async () => {
                                                                   using var response = await PostReceipt(env.Client, ReceiptBody(env, po, w, [RLine(line, env.Product, quantity)]));
                                                                   return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : await response.ErrorCodeAsync());
                                                               })));

            var accepted = results.Count(r => r.StatusCode == HttpStatusCode.Created);
            results.Where(r => r.StatusCode != HttpStatusCode.Created).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict && r.Code == "over_delivery");
            var cap = 10m * (1m + tolerance / 100m);
            (accepted * quantity).ShouldBeLessThanOrEqualTo(cap);
            accepted.ShouldBe((int)Math.Floor(cap / quantity)); // sıradan bağımsız: kabul edilen sayı sığan en büyük sayıdır
            var order = await GetPo(env.Client, po);
            order.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal().ShouldBe(accepted * quantity);
            var physical = 0m;
            foreach (var w in warehouses) physical += await Physical(env, w);
            physical.ShouldBe(accepted * quantity);
            order.GetProperty("receipts").GetArrayLength().ShouldBe(accepted);
        }
    }
}
