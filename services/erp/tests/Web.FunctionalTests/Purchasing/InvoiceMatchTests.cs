using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Infrastructure.Inventory;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class InvoiceMatchTests : TestBase {
    internal sealed record Setup(Env Env, Guid Po, Guid PoLine, Guid Warehouse, JsonElement Receipt);

    internal static async Task<Setup> Received(decimal quantity = 10m, decimal price = 20m, string currency = "TRY", decimal receiveQuantity = -1m, DateOnly? date = null, string[]? modules = null) {
        var env       = await NewPurchasingEnv(null, modules);
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var day       = date ?? Day;
        if (currency != "TRY") await PostingSupport.Override(env, currency, day.ToString("yyyy-MM-dd"), 32m);
        var po   = await CreatePo(env, PoBody(env, [PoLine(env.Product, quantity, price)], currency == "TRY" ? null : new { currency, rate = 30m }, day));
        var id   = po.GetProperty("id").GetGuid();
        await Approve(env.Client, id);
        var line    = (await GetPo(env.Client, id)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receipt = await Receive(env.Client, ReceiptBody(env, id, warehouse, [RLine(line, env.Product, receiveQuantity < 0m ? quantity : receiveQuantity)], day));
        return new Setup(env, id, line, warehouse, receipt);
    }

    internal static async Task<Guid> ReceiveMore(Setup s, decimal quantity, DateOnly? date = null) {
        var receipt = await Receive(s.Env.Client, ReceiptBody(s.Env, s.Po, s.Warehouse, [RLine(s.PoLine, s.Env.Product, quantity)], date));
        return receipt.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Draft(Env env, Guid[] receipts, DateOnly? date = null, string? external = null) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/receipts/invoice-draft", new { receiptIds = receipts, externalNumber = external ?? "TED-" + Guid.NewGuid().ToString("N")[..8], date = (date ?? Day).ToString("yyyy-MM-dd") });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<int> RunWorker(Env env) {
        return TestEnvironment.Factory.Services.GetRequiredService<RevaluationWorker>().PublicRunOnceAsync(CancellationToken.None, env.Tenant);
    }

    [Test]
    public async Task InvoiceDraftFromTwoReceipts_MergesLines() {
        var s       = await Received(10m, 20m, receiveQuantity: 4m);
        var first   = s.Receipt.GetProperty("id").GetGuid();
        var second  = await ReceiveMore(s, 6m);

        var draft = await Draft(s.Env, [first, second]);

        var id = draft.GetProperty("id").GetGuid();
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == id);
        invoice.Status.ShouldBe(InvoiceStatus.Draft);
        invoice.Kind.ShouldBe(InvoiceKind.Purchase);
        invoice.AffectsStock.ShouldBeFalse();
        invoice.SourceType.ShouldBe("purchase-receipt");
        invoice.SourceId.ShouldBe(first);
        invoice.Lines.Count.ShouldBe(2);
        invoice.Lines.Sum(l => l.Quantity).ShouldBe(10m);
        invoice.Lines.All(l => l.UnitPrice == 20m && l.SourceLineId != null).ShouldBeTrue();
        invoice.NetTl.ShouldBe(200m);
    }

    [Test]
    public async Task ReceiptAlreadyInvoiced_NotOfferedAgain() {
        var s       = await Received(10m, 20m, receiveQuantity: 4m);
        var first   = s.Receipt.GetProperty("id").GetGuid();
        var second  = await ReceiveMore(s, 6m);
        await Draft(s.Env, [first]);

        var again = await Draft(s.Env, [first, second]); // yalnızca ikinci mal kabulün miktarı sunulur
        using var none = await s.Env.Client.PostAsJsonAsync($"{Api}/receipts/invoice-draft", new { receiptIds = new[] { first }, externalNumber = "X-1" });

        again.GetProperty("lines").GetArrayLength().ShouldBe(1);
        again.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(6m);
        await Fail(none, HttpStatusCode.Conflict, "nothing_to_invoice");
    }

    [Test]
    public async Task InvoiceRateDifference_CorrectsCostAtReceiptDate_RecostsLaterIssue() {
        var s        = await Received(100m, 10m, "USD");
        var env      = s.Env;
        var receipt  = s.Receipt.GetProperty("id").GetGuid();
        var invoiceDay = Day.AddDays(4);
        await PostingSupport.Override(env, "USD", invoiceDay.ToString("yyyy-MM-dd"), 32.40m);
        using var issue = await StockTestSupport.Op(env.Client, "issue", StockTestSupport.Doc("Issue", Day.AddDays(2), StockTestSupport.Line(s.Warehouse, env.Product, 40m)));
        issue.StatusCode.ShouldBe(HttpStatusCode.OK, await issue.Content.ReadAsStringAsync());
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).TotalCost.ShouldBe(-12800m);

        var draft = await Draft(env, [receipt], invoiceDay);
        await InvoiceTestSupport.Issue(env.Client, draft.GetProperty("id").GetGuid());

        var movements = await StockTestSupport.Movements(env.Tenant);
        var correction = movements.Single(m => m.Type == MovementType.Revaluation);
        correction.Date.ShouldBe(Day);       // mal kabul tarihine yazılır
        correction.TotalCost.ShouldBe(400m); // 100 x 4 TL
        correction.WarehouseId.ShouldBe(s.Warehouse);
        (await RunWorker(env)).ShouldBe(1);
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).UnitCost.ShouldBe(324m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.GoodsReceiptLines.SingleAsync()).AdjustedUnitCostTl.ShouldBe(324m);
    }

    [Test]
    public async Task InvoiceCorrection_RespectsPeriodLock() {
        var s       = await Received(100m, 10m, "USD");
        var env     = s.Env;
        var invoiceDay = Day.AddDays(4);
        await PostingSupport.Override(env, "USD", invoiceDay.ToString("yyyy-MM-dd"), 32.40m);
        var draft = await Draft(env, [s.Receipt.GetProperty("id").GetGuid()], invoiceDay);
        await LockPeriod(env.Client, Day.AddDays(1).ToString("yyyy-MM-dd")); // mal kabul tarihi kilitli, fatura tarihi açık

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.Invoices.SingleAsync()).Status.ShouldBe(InvoiceStatus.Draft); // hiçbir şey yazılmadı
        (await db.StockMovements.CountAsync(m => m.Type == MovementType.Revaluation)).ShouldBe(0);
    }

    [Test]
    public async Task PriceVarianceWarningInMatchRows() {
        var s     = await Received(10m, 20m);
        var env   = s.Env;
        var draft = await Draft(env, [s.Receipt.GetProperty("id").GetGuid()]);
        var id    = draft.GetProperty("id").GetGuid();
        var line  = draft.GetProperty("lines")[0];
        using var put = await env.Client.PutAsJsonAsync($"{Url}/{id}", new {
            kind = "Purchase", branchId = env.Branch, currentAccountId = env.Supplier, date = Day.ToString("yyyy-MM-dd"), currency = "TRY",
            externalNumber = draft.GetProperty("externalNumber").GetString(), version = draft.GetProperty("version").GetUInt32(),
            lines = new[] { new { productId = env.Product, unit = line.GetProperty("unit").GetString(), quantity = 10m, unitPrice = 21m, sourceLineId = line.GetProperty("sourceLineId").GetGuid() } }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        await InvoiceTestSupport.Issue(env.Client, id);

        var po  = await GetPo(env.Client, s.Po);

        var row = po.GetProperty("matchRows")[0];
        row.GetProperty("invoiced").GetDecimal().ShouldBe(10m);
        row.GetProperty("receiptPrice").GetDecimal().ShouldBe(20m);
        row.GetProperty("invoicePrice").GetDecimal().ShouldBe(21m);
        row.GetProperty("priceVariance").GetBoolean().ShouldBeTrue();
        row.GetProperty("qtyMismatch").GetBoolean().ShouldBeFalse();
        po.GetProperty("invoices").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task CancelInvoice_ReversesCorrection() {
        var s       = await Received(100m, 10m, "USD");
        var env     = s.Env;
        var invoiceDay = Day.AddDays(4);
        await PostingSupport.Override(env, "USD", invoiceDay.ToString("yyyy-MM-dd"), 32.40m);
        using var issue = await StockTestSupport.Op(env.Client, "issue", StockTestSupport.Doc("Issue", Day.AddDays(2), StockTestSupport.Line(s.Warehouse, env.Product, 40m)));
        var draft = await Draft(env, [s.Receipt.GetProperty("id").GetGuid()], invoiceDay);
        var id    = draft.GetProperty("id").GetGuid();
        await InvoiceTestSupport.Issue(env.Client, id);
        await RunWorker(env);

        using var cancel = await PostingSupport.Cancel(env.Client, id);

        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        var corrections = (await StockTestSupport.Movements(env.Tenant)).Where(m => m.Type == MovementType.Revaluation).ToList();
        corrections.Sum(m => m.TotalCost).ShouldBe(0m);
        (await RunWorker(env)).ShouldBe(1);
        (await StockTestSupport.Movements(env.Tenant)).Single(m => m.Quantity < 0m).UnitCost.ShouldBe(320m);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.GoodsReceiptLines.SingleAsync()).AdjustedUnitCostTl.ShouldBe(320m);
        // fatura iptalinden sonra mal kabul yeniden faturalanabilir
        (await Draft(env, [s.Receipt.GetProperty("id").GetGuid()], invoiceDay)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task PriceStats_UpdatedOnIssue() {
        var today = Today();
        var s     = await Received(10m, 20m, date: today.AddDays(-5));
        var env   = s.Env;
        using var before = await env.Client.GetAsync($"{Api}/price-stats?productId={env.Product}");
        var draft = await Draft(env, [s.Receipt.GetProperty("id").GetGuid()], today.AddDays(-4));
        var id    = draft.GetProperty("id").GetGuid();
        using var draftStats = await env.Client.GetAsync($"{Api}/price-stats?productId={env.Product}");

        await InvoiceTestSupport.Issue(env.Client, id);
        using var after = await env.Client.GetAsync($"{Api}/price-stats?productId={env.Product}");
        using var noPrice = await (await OtherClient(env, "satinalma.read")).GetAsync($"{Api}/price-stats?productId={env.Product}");

        (await before.JsonAsync()).GetArrayLength().ShouldBe(0);
        (await draftStats.JsonAsync()).GetArrayLength().ShouldBe(0); // taslak sayılmaz
        var stat = (await after.JsonAsync())[0];
        stat.GetProperty("lastUnitPriceTl").GetDecimal().ShouldBe(20m);
        stat.GetProperty("minUnitPriceTl").GetDecimal().ShouldBe(20m);
        stat.GetProperty("maxUnitPriceTl").GetDecimal().ShouldBe(20m);
        stat.GetProperty("lastPurchaseDate").GetString().ShouldBe(today.AddDays(-4).ToString("yyyy-MM-dd"));
        noPrice.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
