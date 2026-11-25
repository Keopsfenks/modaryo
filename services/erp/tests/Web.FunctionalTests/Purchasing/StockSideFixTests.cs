using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

/// <summary>P6 son inceleme düzeltmeleri (stok tarafı): C2 maliyet düzeltmesinin giriş konumunda uygulanması, I2, I3, I5.</summary>
public class StockSideFixTests : TestBase {
    private static Task<int> RunWorker(Env env) {
        return TestEnvironment.Factory.Services.GetRequiredService<RevaluationWorker>().PublicRunOnceAsync(CancellationToken.None, env.Tenant);
    }

    /// <summary>Bugünkü tarihli mal kabul: karantina açık (ya da kapalı), isteğe bağlı FIFO. Serbest bırakma tarihi de bugündür.</summary>
    private static async Task<QuarantineTests.Setup> Received(decimal quantity = 10m, bool quarantine = true, bool fifo = false) {
        var env          = await NewPurchasingEnv();
        var normal       = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var quarantineWh = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "KRN", "Quarantine");
        var damaged      = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "HSR", "Damaged");
        if (fifo) await StockTestSupport.SetStock(env.Client, "fifo");
        if (quarantine) await SetPurchasingSettings(env.Client, 0m, true, new Dictionary<Guid, Guid> { [env.Branch] = quarantineWh });
        var today = Today();
        var po    = await CreatePo(env, PoBody(env, [PoLine(env.Product, quantity, 20m)], null, today));
        var id    = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line    = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receipt = await Receive(env.Client, ReceiptBody(env, id, normal, [RLine(line, env.Product, quantity)], today));
        return new QuarantineTests.Setup(env, id, line, normal, quarantineWh, damaged, receipt, receipt.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> Release(QuarantineTests.Setup s, decimal quantity, Guid? target = null) {
        return s.Env.Client.PostAsJsonAsync($"{Api}/quarantine/release", new { receiptLineId = s.ReceiptLine, quantity, targetWarehouseId = target ?? s.Normal });
    }

    private static async Task<JsonElement> PostLanded(Env env, Guid receipt, decimal amount) {
        var landed = await LandedCostTests.CreateLanded(env, "amount", [receipt], [LandedCostTests.Manual(amount)]);
        return await Post(env.Client, $"landed-costs/{landed.GetProperty("id").GetGuid()}/post");
    }

    private static async Task<List<StockMovement>> Moves(Env env, MovementType type) {
        return (await StockTestSupport.Movements(env.Tenant)).Where(m => m.Type == type).ToList();
    }

    // ---- C2 ---------------------------------------------------------------------------------------------------

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task LandedCost_SameDayQuarantineRelease_FollowsReleasedStock(bool fifo) {
        var s = await Received(10m, true, fifo);
        (await Release(s, 10m)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var posted = await PostLanded(s.Env, s.Receipt.GetProperty("id").GetGuid(), 100m);

        posted.GetProperty("status").GetString().ShouldBe("posted");
        await RunWorker(s.Env);
        (await Moves(s.Env, MovementType.TransferOut)).Single().UnitCost.ShouldBe(30m);
        (await Moves(s.Env, MovementType.TransferIn)).Single().UnitCost.ShouldBe(30m); // 20 + 100 / 10
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        (await db.AverageCosts.SingleAsync(a => a.WarehouseId == s.Normal)).AverageCost.ShouldBe(30m);
    }

    [Test]
    public async Task InvoicePriceDifference_SameDayQuarantineRelease_FollowsReleasedStock() {
        var s = await Received(10m);
        (await Release(s, 10m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var env   = s.Env;
        var draft = await InvoiceMatchTests.Draft(env, [s.Receipt.GetProperty("id").GetGuid()], Today());
        var id    = draft.GetProperty("id").GetGuid();
        var line  = draft.GetProperty("lines")[0];
        using var put = await env.Client.PutAsJsonAsync($"{Url}/{id}", new {
            kind = "Purchase", branchId = env.Branch, currentAccountId = env.Supplier, date = Today().ToString("yyyy-MM-dd"), currency = "TRY",
            externalNumber = draft.GetProperty("externalNumber").GetString(), version = draft.GetProperty("version").GetUInt32(),
            lines = new[] { new { productId = env.Product, unit = line.GetProperty("unit").GetString(), quantity = 10m, unitPrice = 25m, sourceLineId = line.GetProperty("sourceLineId").GetGuid() } }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        await InvoiceTestSupport.Issue(env.Client, id);
        await RunWorker(env);

        (await Moves(env, MovementType.TransferIn)).Single().UnitCost.ShouldBe(25m);
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task LandedCost_SameDayFullIssue_RecostsTheIssue(bool fifo) {
        var s   = await Received(10m, false, fifo);
        var env = s.Env;
        using var issue = await StockTestSupport.Op(env.Client, "issue", StockTestSupport.Doc("Issue", Today(), StockTestSupport.Line(s.Normal, env.Product, 10m)));
        issue.StatusCode.ShouldBe(HttpStatusCode.OK, await issue.Content.ReadAsStringAsync());

        var posted = await PostLanded(env, s.Receipt.GetProperty("id").GetGuid(), 100m);

        posted.GetProperty("status").GetString().ShouldBe("posted");
        await RunWorker(env);
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).UnitCost.ShouldBe(30m);
    }

    // ---- I2 ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task CancelReceipt_WithPostedLandedCost_IsRejected_UntilLandedCostCancelled() {
        var s        = await Received(10m, false);
        var env      = s.Env;
        var receipt  = s.Receipt.GetProperty("id").GetGuid();
        var landed   = await LandedCostTests.CreateLanded(env, "amount", [receipt], [LandedCostTests.Manual(100m)]);
        var landedId = landed.GetProperty("id").GetGuid();
        await Post(env.Client, $"landed-costs/{landedId}/post");

        using var blocked = await env.Client.PostAsJsonAsync($"{Api}/receipts/{receipt}/cancel", new { reason = "Yanlış giriş" });

        await StockTestSupport.Fail(blocked, HttpStatusCode.Conflict, "receipt_has_landed_cost");
        (await Physical(env, s.Normal)).ShouldBe(10m);

        await Post(env.Client, $"landed-costs/{landedId}/cancel", new { reason = "Vazgeçildi" });
        var cancelled = await Post(env.Client, $"receipts/{receipt}/cancel", new { reason = "Yanlış giriş" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        (await Physical(env, s.Normal)).ShouldBe(0m);
    }

    [Test]
    public async Task CancelReceipt_WithOtherNetCostCorrection_IsRejectedAsProcessed() {
        var s       = await Received(10m, false);
        var env     = s.Env;
        var inbound = (await StockTestSupport.Movements(env.Tenant)).Single();
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "correct", new {
            warehouseId = s.Normal, productId = env.Product, variantId = (Guid?)null, date = Today().ToString("yyyy-MM-dd"), inboundMovementId = inbound.Id,
            deltaTotalTl = 50m, sourceType = "manual", sourceId = Guid.CreateVersion7()
        }));

        using var blocked = await env.Client.PostAsJsonAsync($"{Api}/receipts/{s.Receipt.GetProperty("id").GetGuid()}/cancel", new { reason = "Yanlış giriş" });

        await StockTestSupport.Fail(blocked, HttpStatusCode.Conflict, "receipt_processed");
    }

    // ---- I3 ---------------------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> PostReturn(QuarantineTests.Setup s, decimal quantity) {
        return s.Env.Client.PostAsJsonAsync($"{Api}/returns", new {
            receiptId = s.Receipt.GetProperty("id").GetGuid(), date = Today().ToString("yyyy-MM-dd"), reason = "Hatalı mal", createInvoice = false,
            lines = new[] { new { receiptLineId = s.ReceiptLine, quantity } }
        });
    }

    [Test]
    public async Task Return_ExcludesQuantityRejectedToDamaged() {
        var s = await Received(10m);
        using var reject = await s.Env.Client.PostAsJsonAsync($"{Api}/quarantine/reject", new { receiptLineId = s.ReceiptLine, quantity = 4m, action = "damaged" });
        reject.StatusCode.ShouldBe(HttpStatusCode.OK, await reject.Content.ReadAsStringAsync());

        using var tooMuch = await PostReturn(s, 7m);
        using var fits    = await PostReturn(s, 6m);

        await StockTestSupport.Fail(tooMuch, HttpStatusCode.BadRequest, "return_exceeds_received");
        fits.StatusCode.ShouldBe(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
        (await Physical(s.Env, s.Damaged)).ShouldBe(4m); // hasarlı depodaki mal iade edilmedi
        (await Physical(s.Env, s.Quarantine)).ShouldBe(0m);
    }

    [Test]
    public async Task Return_TakesEachPortionFromWhereItIsNow() {
        var s      = await Received(10m);
        var second = await InventoryTestSupport.CreateWarehouse(s.Env.Client, s.Env.Branch, "D2");
        (await Release(s, 4m)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Release(s, 6m, second)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var first = await PostReturn(s, 5m); // ilk serbest bırakılan depodan 4, ikincisinden 1
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        (await Physical(s.Env, s.Normal)).ShouldBe(0m);
        (await Physical(s.Env, second)).ShouldBe(5m);

        using var rest = await PostReturn(s, 5m);
        rest.StatusCode.ShouldBe(HttpStatusCode.OK, await rest.Content.ReadAsStringAsync());
        (await Physical(s.Env, second)).ShouldBe(0m);
        using var more = await PostReturn(s, 1m);
        await StockTestSupport.Fail(more, HttpStatusCode.BadRequest, "return_exceeds_received");
        var returns = await Moves(s.Env, MovementType.PurchaseReturn);
        returns.Where(m => m.WarehouseId == s.Normal).Sum(m => -m.Quantity).ShouldBe(4m);
        returns.Where(m => m.WarehouseId == second).Sum(m => -m.Quantity).ShouldBe(6m);
    }

    // ---- I5 ---------------------------------------------------------------------------------------------------

    [Test]
    public async Task CancelPo_ReleasesExpectedReservationsAndEmitsEvent() {
        var env     = await NewPurchasingEnv(null, ["siparise-gore-tedarik"]);
        var holding = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "BEK");
        var po      = (await CreatePo(env, PoBody(env, [PoLine(env.Product, 100m, 20m)]))).GetProperty("id").GetGuid();
        await Approve(env.Client, po);
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "reserve", StockTestSupport.ReserveDoc("sales-order", Guid.CreateVersion7(),
                                                          StockTestSupport.RLine(holding, env.Product, 100m, Guid.CreateVersion7(), true, po))));

        var cancelled = await Post(env.Client, $"orders/{po}/cancel", new { reason = "Vazgeçildi" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        (await StockTestSupport.Reservations(env.Tenant)).Single().Status.ShouldBe(ReservationStatus.Released);
        (await StockTestSupport.OutboxCount(env.Tenant, "purchase.order-cancelled")).ShouldBe(1);
    }
}
