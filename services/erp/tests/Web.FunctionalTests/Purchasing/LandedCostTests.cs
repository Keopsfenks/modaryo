using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;
using ERP.Infrastructure.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class LandedCostTests : TestBase {
    private static Task<HttpResponseMessage> PostLanded(Env env, string key, Guid[] receipts, object[] sources, Guid? importFile = null, DateOnly? date = null) {
        return env.Client.PostAsJsonAsync($"{Api}/landed-costs", new { date = (date ?? Day.AddDays(1)).ToString("yyyy-MM-dd"), key, receiptIds = receipts, importFileId = importFile, sources });
    }

    internal static object Manual(decimal amount, string type = "navlun") => new { manualAmountTl = amount, costType = type, description = "Masraf" };

    internal static async Task<JsonElement> CreateLanded(Env env, string key, Guid[] receipts, object[] sources, Guid? importFile = null) {
        using var response = await PostLanded(env, key, receipts, sources, importFile);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<int> RunWorker(Env env) {
        return TestEnvironment.Factory.Services.GetRequiredService<RevaluationWorker>().PublicRunOnceAsync(CancellationToken.None, env.Tenant);
    }

    [Test]
    public async Task WeightKeyMissingWeight_Returns409() {
        var s = await InvoiceMatchTests.Received(10m, 10m);

        using var response = await PostLanded(s.Env, "weight", [s.Receipt.GetProperty("id").GetGuid()], [Manual(100m)]);

        await Fail(response, HttpStatusCode.Conflict, "weight_missing");
        (await response.Content.ReadAsStringAsync()).ShouldContain("P1"); // eksik kalem kodları listelenir
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        (await db.LandedCosts.CountAsync()).ShouldBe(0);
        (await db.StockMovements.CountAsync(m => m.Type == MovementType.Revaluation)).ShouldBe(0);
    }

    [Test]
    public async Task WeightKey_WithWeights_Allocates() {
        var s = await InvoiceMatchTests.Received(10m, 10m);
        await using (var db = PricingTestSupport.Db(s.Env.Tenant)) {
            (await db.Products.FindAsync(s.Env.Product))!.WeightKg = 2m;
            await db.SaveChangesAsync();
        }

        var landed = await CreateLanded(s.Env, "weight", [s.Receipt.GetProperty("id").GetGuid()], [Manual(100m)]);

        landed.GetProperty("allocations")[0].GetProperty("allocatedTl").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task AmountKey_SumsExactly() {
        var s  = await InvoiceMatchTests.Received(3m, 10m, receiveQuantity: 1m);
        var r1 = s.Receipt.GetProperty("id").GetGuid();
        var r2 = await InvoiceMatchTests.ReceiveMore(s, 1m);
        var r3 = await InvoiceMatchTests.ReceiveMore(s, 1m);

        var landed = await CreateLanded(s.Env, "amount", [r1, r2, r3], [Manual(100m)]);

        var shares = landed.GetProperty("allocations").EnumerateArray().Select(a => a.GetProperty("allocatedTl").GetDecimal()).OrderBy(x => x).ToList();
        shares.ShouldBe([33.33m, 33.33m, 33.34m]);
        shares.Sum().ShouldBe(100m);
        landed.GetProperty("totalTl").GetDecimal().ShouldBe(100m);
        landed.GetProperty("status").GetString().ShouldBe("draft");
        landed.GetProperty("number").GetString()!.ShouldStartWith("MD" + Day.Year);
    }

    [Test]
    public async Task Post_RaisesUnitCostAtReceiptDate() {
        var s       = await InvoiceMatchTests.Received(100m, 10m);
        var env     = s.Env;
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        using var issue = await StockTestSupport.Op(env.Client, "issue", StockTestSupport.Doc("Issue", Day.AddDays(2), StockTestSupport.Line(s.Warehouse, env.Product, 40m)));
        issue.StatusCode.ShouldBe(HttpStatusCode.OK, await issue.Content.ReadAsStringAsync());
        var landed = await CreateLanded(env, "amount", [receipt], [Manual(150m, "navlun"), Manual(50m, "gumruk")]);
        var id     = landed.GetProperty("id").GetGuid();
        (await StockTestSupport.Movements(env.Tenant)).Count(m => m.Type == MovementType.Revaluation).ShouldBe(0); // taslak maliyeti etkilemez

        var posted = await Post(env.Client, $"landed-costs/{id}/post");

        posted.GetProperty("status").GetString().ShouldBe("posted");
        var correction = (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Type == MovementType.Revaluation);
        correction.Date.ShouldBe(Day); // mal kabul tarihi
        correction.TotalCost.ShouldBe(200m);
        (await RunWorker(env)).ShouldBe(1);
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).UnitCost.ShouldBe(12m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.GoodsReceiptLines.SingleAsync()).AdjustedUnitCostTl.ShouldBe(12m);
        using var again = await env.Client.PostAsJsonAsync($"{Api}/landed-costs/{id}/post", new { });
        await Fail(again, HttpStatusCode.Conflict, "landed_cost_not_draft");
        var detail = await (await env.Client.GetAsync($"{Api}/landed-costs/{id}")).JsonAsync();
        detail.GetProperty("byCostType").GetProperty("navlun").GetDecimal().ShouldBe(150m);
        detail.GetProperty("allocations")[0].GetProperty("byCostType").GetProperty("gumruk").GetDecimal().ShouldBe(50m);
    }

    [Test]
    public async Task CancelLanded_ReversesCost() {
        var s   = await InvoiceMatchTests.Received(100m, 10m);
        var env = s.Env;
        using var issue = await StockTestSupport.Op(env.Client, "issue", StockTestSupport.Doc("Issue", Day.AddDays(2), StockTestSupport.Line(s.Warehouse, env.Product, 40m)));
        var id = (await CreateLanded(env, "amount", [s.Receipt.GetProperty("id").GetGuid()], [Manual(200m)])).GetProperty("id").GetGuid();
        await Post(env.Client, $"landed-costs/{id}/post");
        await RunWorker(env);

        var cancelled = await Post(env.Client, $"landed-costs/{id}/cancel", new { reason = "Hatalı dağıtım" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        (await StockTestSupport.Movements(env.Tenant)).Where(m => m.Type == MovementType.Revaluation).Sum(m => m.TotalCost).ShouldBe(0m);
        (await RunWorker(env)).ShouldBe(1);
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).UnitCost.ShouldBe(10m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.GoodsReceiptLines.SingleAsync()).AdjustedUnitCostTl.ShouldBe(10m);
    }

    [Test]
    public async Task Post_RespectsPeriodLock_AtReceiptDate() {
        var s   = await InvoiceMatchTests.Received(100m, 10m);
        var env = s.Env;
        var id  = (await CreateLanded(env, "amount", [s.Receipt.GetProperty("id").GetGuid()], [Manual(200m)])).GetProperty("id").GetGuid();
        await LockPeriod(env.Client, Day.AddDays(1).ToString("yyyy-MM-dd")); // masraf tarihi açık, mal kabul tarihi kilitli

        using var response = await env.Client.PostAsJsonAsync($"{Api}/landed-costs/{id}/post", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.LandedCosts.SingleAsync()).Status.ShouldBe(LandedCostStatus.Draft);
        (await db.StockMovements.CountAsync(m => m.Type == MovementType.Revaluation)).ShouldBe(0);
    }

    [Test]
    public async Task SourceInvoiceCancelled_Returns409() {
        var s       = await InvoiceMatchTests.Received(10m, 10m);
        var env     = s.Env;
        var invoice = (await CreateAndIssue(env, Body(env, "Purchase", [FreeLine("Navlun", 1m, 200m, 20)]))).GetProperty("id").GetGuid();
        using var cancel = await PostingSupport.Cancel(env.Client, invoice);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        using var response = await PostLanded(env, "amount", [s.Receipt.GetProperty("id").GetGuid()], [new { invoiceId = invoice, costType = "navlun" }]);

        await Fail(response, HttpStatusCode.Conflict, "landed_cost_source_cancelled");
    }

    [Test]
    public async Task InvoiceSource_UsesNetAmount_AndBlocksInvoiceCancelWhilePosted() {
        var s       = await InvoiceMatchTests.Received(10m, 10m);
        var env     = s.Env;
        var invoice = (await CreateAndIssue(env, Body(env, "Purchase", [FreeLine("Navlun", 1m, 200m, 20)]))).GetProperty("id").GetGuid();
        var landed  = await CreateLanded(env, "amount", [s.Receipt.GetProperty("id").GetGuid()], [new { invoiceId = invoice, costType = "navlun" }]);
        var id      = landed.GetProperty("id").GetGuid();
        landed.GetProperty("totalTl").GetDecimal().ShouldBe(200m); // KDV hariç
        await Post(env.Client, $"landed-costs/{id}/post");

        using var blocked = await PostingSupport.Cancel(env.Client, invoice);
        await Post(env.Client, $"landed-costs/{id}/cancel", new { reason = "Vazgeçildi" });
        using var allowed = await PostingSupport.Cancel(env.Client, invoice);

        await Fail(blocked, HttpStatusCode.Conflict, "invoice_in_landed_cost");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task CancelledReceiptTarget_Returns409() {
        var s       = await InvoiceMatchTests.Received(10m, 10m);
        var env     = s.Env;
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        await Post(env.Client, $"receipts/{receipt}/cancel", new { reason = "Hatalı giriş" });

        using var response = await PostLanded(env, "amount", [receipt], [Manual(100m)]);

        await Fail(response, HttpStatusCode.Conflict, "landed_cost_target_cancelled");
    }

    [Test]
    public async Task Prices_RequireAlis() {
        var s = await InvoiceMatchTests.Received(10m, 10m);
        using var noPrice = await OtherClient(s.Env, "satinalma.read", "satinalma.manage");

        using var response = await noPrice.PostAsJsonAsync($"{Api}/landed-costs", new { date = Day.ToString("yyyy-MM-dd"), key = "amount", receiptIds = new[] { s.Receipt.GetProperty("id").GetGuid() }, sources = new[] { Manual(10m) } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
