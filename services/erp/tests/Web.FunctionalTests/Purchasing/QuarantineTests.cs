using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class QuarantineTests : TestBase {
    internal sealed record Setup(Env Env, Guid Po, Guid PoLine, Guid Normal, Guid Quarantine, Guid Damaged, JsonElement Receipt, Guid ReceiptLine);

    internal static async Task<Setup> Held(decimal quantity = 10m, string[]? modules = null, bool enabled = true, Guid? product = null) {
        var env        = await NewPurchasingEnv(null, modules);
        var normal     = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var quarantine = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "KRN", "Quarantine");
        var damaged    = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "HSR", "Damaged");
        if (enabled) await SetPurchasingSettings(env.Client, 0m, true, new Dictionary<Guid, Guid> { [env.Branch] = quarantine });
        var item = product ?? env.Product;
        var po   = await CreatePo(env, PoBody(env, [PoLine(item, quantity, 20m)]));
        var id   = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line    = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receipt = await Receive(env.Client, ReceiptBody(env, id, normal, [RLine(line, item, quantity)]));
        return new Setup(env, id, line, normal, quarantine, damaged, receipt, receipt.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> Release(Setup s, decimal quantity, Guid? target = null) {
        return s.Env.Client.PostAsJsonAsync($"{Api}/quarantine/release", new { receiptLineId = s.ReceiptLine, quantity, targetWarehouseId = target ?? s.Normal });
    }

    private static Task<HttpResponseMessage> Reject(Setup s, decimal quantity, string action) {
        return s.Env.Client.PostAsJsonAsync($"{Api}/quarantine/reject", new { receiptLineId = s.ReceiptLine, quantity, action });
    }

    [Test]
    public async Task QuarantineReceipt_NotAvailableUntilReleased() {
        var s = await Held();

        // Karantinaya giren mal fiziksel stoktur ama kullanılabilir değildir; satış rezervasyonu onu kullanamaz.
        (await Physical(s.Env, s.Quarantine)).ShouldBe(10m);
        (await Available(s.Env, s.Quarantine)).ShouldBe(0m);
        (await Available(s.Env, s.Normal)).ShouldBe(0m);
        s.Receipt.GetProperty("lines")[0].GetProperty("quarantine").GetString().ShouldBe("held");
        using var blocked = await StockTestSupport.Op(s.Env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                                                                      StockTestSupport.RLine(s.Quarantine, s.Env.Product, 1m, Guid.CreateVersion7())));
        using var listed = await s.Env.Client.GetAsync($"{Api}/quarantine?branchId={s.Env.Branch}");
        var rows = await listed.JsonAsync();

        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("openQuantity").GetDecimal().ShouldBe(10m);
        rows[0].EnumerateObject().Select(p => p.Name).ShouldNotContain("unitPrice");

        using var released = await Release(s, 10m);
        released.StatusCode.ShouldBe(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        (await Available(s.Env, s.Normal)).ShouldBe(10m);
        await StockTestSupport.Ok(StockTestSupport.Op(s.Env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                                                                      StockTestSupport.RLine(s.Normal, s.Env.Product, 4m, Guid.CreateVersion7()))));
        await StockTestSupport.Fail(blocked, HttpStatusCode.Conflict, "insufficient_stock");
    }

    [Test]
    public async Task Release_MovesToNormalWarehouse() {
        var s = await Held();

        var       first  = await (await Release(s, 4m)).JsonAsync();
        using var damagedTarget = await Release(s, 1m, s.Damaged);
        using var tooMuch = await Release(s, 7m);
        var       second = await (await Release(s, 6m)).JsonAsync();
        using var again  = await Release(s, 1m);

        first.GetProperty("state").GetString().ShouldBe("held");
        first.GetProperty("openQuantity").GetDecimal().ShouldBe(6m);
        await Fail(damagedTarget, HttpStatusCode.BadRequest, "target_not_normal");
        await Fail(tooMuch, HttpStatusCode.Conflict, "quarantine_exceeds_open");
        second.GetProperty("state").GetString().ShouldBe("released");
        await Fail(again, HttpStatusCode.Conflict, "quarantine_not_held");
        (await Physical(s.Env, s.Quarantine)).ShouldBe(0m);
        (await Physical(s.Env, s.Normal)).ShouldBe(10m);
        var movements = await StockTestSupport.Movements(s.Env.Tenant);
        movements.Count(m => m.Type == MovementType.TransferOut).ShouldBe(2);
        movements.Where(m => m.Type == MovementType.TransferIn).Sum(m => m.UnitCost * m.Quantity).ShouldBe(200m);
    }

    [Test]
    public async Task RejectDamaged() {
        var s = await Held();

        var rejected = await (await Reject(s, 3m, "damaged")).JsonAsync();

        rejected.GetProperty("openQuantity").GetDecimal().ShouldBe(7m);
        (await Physical(s.Env, s.Damaged)).ShouldBe(3m);
        (await Physical(s.Env, s.Quarantine)).ShouldBe(7m);
        (await Available(s.Env, s.Damaged)).ShouldBe(0m);
        var all = await (await Reject(s, 7m, "damaged")).JsonAsync();
        all.GetProperty("state").GetString().ShouldBe("rejected");
    }

    [Test]
    public async Task RejectReturn_CreatesReturnDraft() {
        var s = await Held();
        var original = await IssuedPurchaseInvoice(s, 10m);

        var result = await (await Reject(s, 4m, "return")).JsonAsync();

        result.GetProperty("openQuantity").GetDecimal().ShouldBe(6m);
        (await Physical(s.Env, s.Quarantine)).ShouldBe(6m);
        var invoiceId = result.GetProperty("invoiceId").GetGuid();
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        var draft = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        draft.Kind.ShouldBe(InvoiceKind.PurchaseReturn);
        draft.Status.ShouldBe(InvoiceStatus.Draft);
        draft.AffectsStock.ShouldBeFalse();
        draft.ReturnOfInvoiceId.ShouldBe(original);
        draft.Lines.Single().Quantity.ShouldBe(4m);
        (await StockTestSupport.Movements(s.Env.Tenant)).Count(m => m.Type == MovementType.PurchaseReturn).ShouldBe(1);
    }

    [Test]
    public async Task RejectReturn_WithoutInvoice_StillReturnsStock() {
        var s = await Held();

        var result = await (await Reject(s, 4m, "return")).JsonAsync();

        result.GetProperty("invoiceId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Physical(s.Env, s.Quarantine)).ShouldBe(6m);
    }

    [Test]
    public async Task QuarantineDisabled_Returns409() {
        var s = await Held(enabled: false);

        using var release = await Release(s, 1m);
        using var reject  = await Reject(s, 1m, "damaged");

        await Fail(release, HttpStatusCode.Conflict, "quarantine_not_enabled");
        await Fail(reject, HttpStatusCode.Conflict, "quarantine_not_enabled");
        s.Receipt.GetProperty("lines")[0].GetProperty("quarantine").GetString().ShouldBe("none");
    }

    [Test]
    public async Task Release_FulfilsBackToBackReservations() {
        var s = await Held(10m, ["siparise-gore-tedarik"]);
        // Karantina kabulünde rezervasyon taşınmaz; bu yüzden sipariş sonradan rezerve edilip kabul yeniden yapılmaz: yeni kabul karantinada kalır.
        var holding = await InventoryTestSupport.CreateWarehouse(s.Env.Client, s.Env.Branch, "BEK");
        var po2     = await CreatePo(s.Env, PoBody(s.Env, [PoLine(s.Env.Product, 10m, 20m)]));
        var id2     = po2.GetProperty("id").GetGuid();
        await Approve(s.Env.Client, id2);
        var line2 = (await GetPo(s.Env.Client, id2)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        await StockTestSupport.Ok(StockTestSupport.Op(s.Env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                                                                      StockTestSupport.RLine(holding, s.Env.Product, 10m, Guid.CreateVersion7(), true, id2))));
        var receipt = await Receive(s.Env.Client, ReceiptBody(s.Env, id2, s.Normal, [RLine(line2, s.Env.Product, 10m)]));
        var lineId  = receipt.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await StockTestSupport.Reservations(s.Env.Tenant)).Single().WarehouseId.ShouldBe(holding); // karantinadayken taşınmadı

        using var released = await s.Env.Client.PostAsJsonAsync($"{Api}/quarantine/release", new { receiptLineId = lineId, quantity = 6m, targetWarehouseId = s.Normal });

        released.StatusCode.ShouldBe(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        var rows = await StockTestSupport.Reservations(s.Env.Tenant);
        rows.Where(r => r.WarehouseId == s.Normal).Sum(r => r.Quantity).ShouldBe(6m);
        rows.Single(r => r.WarehouseId == holding).Quantity.ShouldBe(4m);
    }

    [Test]
    public async Task Release_TrackedLot_DerivesLotsFromReceipt() {
        var env  = await NewPurchasingEnv(null, ["seri-lot"]);
        var lot  = await InventoryTestSupport.CreateProduct(env.Client, "LOT-9", new { tracksSerialLot = true, trackingMode = "lot", vatRate = 20, baseUnit = "adet" });
        var normal     = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var quarantine = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "KRN", "Quarantine");
        await SetPurchasingSettings(env.Client, 0m, true, new Dictionary<Guid, Guid> { [env.Branch] = quarantine });
        var po = await CreatePo(env, PoBody(env, [PoLine(lot, 10m, 5m)]));
        var id = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var poLine  = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receipt = await Receive(env.Client, ReceiptBody(env, id, normal, [RLine(poLine, lot, 10m, new { lots = new[] { new { lotNo = "A", quantity = 6m }, new { lotNo = "B", quantity = 4m } } })]));
        var lineId  = receipt.GetProperty("lines")[0].GetProperty("id").GetGuid();

        using var first  = await env.Client.PostAsJsonAsync($"{Api}/quarantine/release", new { receiptLineId = lineId, quantity = 7m, targetWarehouseId = normal });
        using var second = await env.Client.PostAsJsonAsync($"{Api}/quarantine/release", new { receiptLineId = lineId, quantity = 3m, targetWarehouseId = normal });

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        (await Physical(env, normal, lot)).ShouldBe(10m);
        (await Physical(env, quarantine, lot)).ShouldBe(0m);
    }

    /// <summary>Mal kabule bağlı, kesilmiş bir alış faturası (stoksuz) kurar; asıl fatura kimliğini döndürür.</summary>
    internal static async Task<Guid> IssuedPurchaseInvoice(Setup s, decimal quantity) {
        var env     = s.Env;
        var invoice = await CreateAndIssue(env, Body(env, "Purchase", [ProductLine(env, quantity, 20m)]));
        var id      = invoice.GetProperty("id").GetGuid();
        await using var db = PricingTestSupport.Db(env.Tenant);
        var row = await db.Invoices.FindAsync(id);
        row!.SourceType = "purchase-receipt";
        row.SourceId    = s.Receipt.GetProperty("id").GetGuid();
        await db.SaveChangesAsync();
        return id;
    }
}
