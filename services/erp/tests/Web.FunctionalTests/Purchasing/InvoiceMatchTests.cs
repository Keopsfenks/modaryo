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

    internal static async Task<JsonElement> Draft(Env env, Guid[] receipts, DateOnly? date = null, string? external = null) {
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

    // ---- P6 son inceleme: fatura tarafı düzeltmeleri -------------------------------------------------------------------

    private static Task<HttpResponseMessage> PutDraft(Env env, JsonElement draft, decimal? quantity = null, bool affectsStock = false, Guid? supplier = null,
                                                      string? currency = null, Guid? sourceLineId = null, Guid? warehouse = null) {
        var line = draft.GetProperty("lines")[0];
        return env.Client.PutAsJsonAsync($"{Url}/{draft.GetProperty("id").GetGuid()}", new {
            kind = "Purchase", branchId = env.Branch, currentAccountId = supplier ?? env.Supplier, date = Day.ToString("yyyy-MM-dd"), currency = currency ?? "TRY",
            externalNumber = draft.GetProperty("externalNumber").GetString(), affectsStock, warehouseId = warehouse,
            lines = new[] { new { productId = env.Product, unit = line.GetProperty("unit").GetString(), quantity = quantity ?? line.GetProperty("quantity").GetDecimal(),
                                  unitPrice = line.GetProperty("unitPrice").GetDecimal(), sourceLineId = sourceLineId ?? line.GetProperty("sourceLineId").GetGuid() } }
        });
    }

    private static async Task<JsonElement> PutOk(Env env, JsonElement draft, decimal quantity) {
        using var response = await PutDraft(env, draft, quantity);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> TryDraft(Env env, Guid receipt) {
        return env.Client.PostAsJsonAsync($"{Api}/receipts/invoice-draft", new { receiptIds = new[] { receipt }, externalNumber = "TED-" + Guid.NewGuid().ToString("N")[..8], date = Day.ToString("yyyy-MM-dd") });
    }

    [Test]
    public async Task ReceiptDraft_CannotSwitchToAffectsStock() {
        var s     = await Received(10m, 20m);
        var draft = await Draft(s.Env, [s.Receipt.GetProperty("id").GetGuid()]);

        using var response = await PutDraft(s.Env, draft, affectsStock: true, warehouse: s.Warehouse);

        await Fail(response, HttpStatusCode.BadRequest, "receipt_invoice_affects_stock");
    }

    [Test]
    public async Task ReceiptDraft_SupplierCurrencyAndLinkAreLocked() {
        var s       = await Received(10m, 20m, receiveQuantity: 4m);
        var env     = s.Env;
        var first   = s.Receipt.GetProperty("id").GetGuid();
        var second  = await ReceiveMore(s, 6m);
        var draft   = await Draft(env, [first]);
        var other   = await CreateCari(env.Client, "SUP2", false, true);
        using var secondGet = await env.Client.GetAsync($"{Api}/receipts/{second}");
        var foreign = (await secondGet.JsonAsync()).GetProperty("lines")[0].GetProperty("id").GetGuid();

        using var supplier = await PutDraft(env, draft, supplier: other);
        using var currency = await PutDraft(env, draft, currency: "USD");
        using var link     = await PutDraft(env, draft, sourceLineId: foreign);

        await Fail(supplier, HttpStatusCode.BadRequest, "receipt_invoice_locked");
        await Fail(currency, HttpStatusCode.BadRequest, "receipt_invoice_locked");
        await Fail(link, HttpStatusCode.BadRequest, "receipt_invoice_locked");
    }

    [Test]
    public async Task ReceiptDraft_Issue_RejectsTamperedStockFlagAndSupplier() {
        var s     = await Received(10m, 20m);
        var env   = s.Env;
        var draft = await Draft(env, [s.Receipt.GetProperty("id").GetGuid()]);
        var id    = draft.GetProperty("id").GetGuid();
        var other = await CreateCari(env.Client, "SUP3", false, true);

        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var row = await db.Invoices.SingleAsync(i => i.Id == id);
            row.AffectsStock = true;
            row.WarehouseId  = s.Warehouse;
            await db.SaveChangesAsync();
        }

        using var stockFlag = await env.Client.PostAsync($"{Url}/{id}/issue", null);
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var row = await db.Invoices.SingleAsync(i => i.Id == id);
            row.AffectsStock     = false;
            row.WarehouseId      = null;
            row.CurrentAccountId = other;
            await db.SaveChangesAsync();
        }

        using var supplier = await env.Client.PostAsync($"{Url}/{id}/issue", null);

        await Fail(stockFlag, HttpStatusCode.Conflict, "receipt_invoice_affects_stock");
        await Fail(supplier, HttpStatusCode.Conflict, "receipt_invoice_locked");
        await using var check = PricingTestSupport.Db(env.Tenant);
        (await check.Invoices.SingleAsync(i => i.Id == id)).Status.ShouldBe(InvoiceStatus.Draft);
        (await check.StockMovements.CountAsync(m => m.SourceType == "invoice")).ShouldBe(0);
    }

    [Test]
    public async Task ReceiptDraft_EditedQuantityCannotExceedReceipt() {
        var s     = await Received(10m, 20m);
        var draft = await Draft(s.Env, [s.Receipt.GetProperty("id").GetGuid()]);

        using var response = await PutDraft(s.Env, draft, 20m);

        await Fail(response, HttpStatusCode.Conflict, "receipt_over_invoiced");
    }

    [Test]
    public async Task ReceiptDraft_OtherDraftsCountTowardsInvoicedQuantity() {
        var s       = await Received(10m, 20m);
        var env     = s.Env;
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        var first   = await PutOk(env, await Draft(env, [receipt]), 3m);
        var second  = await Draft(env, [receipt]); // kalan 7

        using var tooMuch = await PutDraft(env, first, 5m); // 5 + 7 > 10
        var       fits    = await PutOk(env, first, 3m);

        second.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(7m);
        await Fail(tooMuch, HttpStatusCode.Conflict, "receipt_over_invoiced");
        fits.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(3m);
    }

    [Test]
    public async Task ReceiptDraft_Issue_RejectsOverInvoicedQuantity() {
        var s        = await Received(10m, 20m);
        var env      = s.Env;
        var receipt  = s.Receipt.GetProperty("id").GetGuid();
        await PutOk(env, await Draft(env, [receipt]), 3m);
        var secondId = (await Draft(env, [receipt])).GetProperty("id").GetGuid();
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var line = await db.InvoiceLines.SingleAsync(l => l.InvoiceId == secondId);
            line.Quantity = 9m; // doğrudan veritabanında bozulmuş taslak: 3 + 9 > 10
            await db.SaveChangesAsync();
        }

        using var response = await env.Client.PostAsync($"{Url}/{secondId}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "receipt_over_invoiced");
        await using var check = PricingTestSupport.Db(env.Tenant);
        (await check.Invoices.SingleAsync(i => i.Id == secondId)).Status.ShouldBe(InvoiceStatus.Draft);
    }

    [Test]
    public async Task ConcurrentDraftCreation_InvoicesReceiptOnlyOnce() {
        var s       = await Received(10m, 20m);
        var receipt = s.Receipt.GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => TryDraft(s.Env, receipt)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        (await db.InvoiceLines.SumAsync(l => l.Quantity)).ShouldBe(10m);
    }

    [Test]
    public async Task ConcurrentDraftEdits_ExactlyOneFits() {
        var s       = await Received(10m, 20m);
        var env     = s.Env;
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        var first   = await PutOk(env, await Draft(env, [receipt]), 3m);
        var second  = await PutOk(env, await Draft(env, [receipt]), 3m); // toplam 6, 4 boşta

        // Kilitsiz: ikisi de diğerini 3 görür (6 + 3 = 9 sığar), toplam 12 olurdu. Kilitli: ikincisi 6 + 6 = 12'yi görür ve reddedilir.
        var responses = await Task.WhenAll(PutDraft(env, first, 6m), PutDraft(env, second, 6m));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.InvoiceLines.SumAsync(l => l.Quantity)).ShouldBeLessThanOrEqualTo(10m);
    }

    [Test]
    public async Task PriceVariance_ComparesInPoUnit_WhenReceivedInAnotherUnit() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var item      = await InventoryTestSupport.CreateProduct(env.Client, "KOLI7", new { baseUnit = "adet", vatRate = 20, units = new[] { new { unit = "koli", factor = 12m, barcode = (string?)null } } });
        var po        = await CreatePo(env, PoBody(env, [PoLine(item, 10m, 120m, new { unit = "koli" })]));
        var poId      = po.GetProperty("id").GetGuid();
        await Approve(env.Client, poId);
        var poLine  = (await GetPo(env.Client, poId)).GetProperty("lines")[0].GetProperty("id").GetGuid();
        var receipt = await Receive(env.Client, ReceiptBody(env, poId, warehouse, [RLine(poLine, item, 120m, new { unit = "adet" })]));
        var draft   = await Draft(env, [receipt.GetProperty("id").GetGuid()]);
        await InvoiceTestSupport.Issue(env.Client, draft.GetProperty("id").GetGuid());

        var row = (await GetPo(env.Client, poId)).GetProperty("matchRows")[0];

        row.GetProperty("poPrice").GetDecimal().ShouldBe(120m);
        row.GetProperty("receiptPrice").GetDecimal().ShouldBe(120m); // koli başına; adet başına 10 değil
        row.GetProperty("invoicePrice").GetDecimal().ShouldBe(120m);
        row.GetProperty("priceVariance").GetBoolean().ShouldBeFalse();
    }
}
