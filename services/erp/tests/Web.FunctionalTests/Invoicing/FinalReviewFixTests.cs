using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.PostingSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

/// <summary>P4 son inceleme düzeltmeleri: C1 (numara biçimi + yıl devri), I1, I2, I3, I5, I6.</summary>
public class FinalReviewFixTests : TestBase {
    private static readonly DateOnly NextYear = new(2027, 1, 5);

    private static async Task<JsonElement> SeriesList(Env env) {
        return await (await env.Client.GetAsync(SeriesUrl)).JsonAsync();
    }

    private static async Task<string> NumberOf(Env env, Guid id) {
        return (await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("number").GetString()!;
    }

    private static async Task<(HttpStatusCode Status, string Body)[]> IssueInParallel(Env env, IEnumerable<Guid> ids) {
        return await Task.WhenAll(ids.Select(id => Task.Run(async () => {
                                                                using var response = await env.Client.PostAsync($"{Url}/{id}/issue", null);
                                                                return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
                                                            })));
    }

    // ---- C1: tek biçim (3 harf + yıl + 9 hane), yıl devri ---------------------------------------------------------

    [Test]
    public async Task Paper_Number_Is16CharsWithYear() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");

        var issued = await CreateAndIssue(env, Body(env));

        issued.GetProperty("number").GetString().ShouldBe("FTR2026000000001");
        issued.GetProperty("number").GetString()!.Length.ShouldBe(16);
    }

    [Test]
    public async Task Paper_YearRollover_CreatesSamePrefixSeriesStartingAtOne() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        (await CreateAndIssue(env, Body(env))).GetProperty("number").GetString().ShouldBe("FTR2026000000001");
        (await CreateAndIssue(env, Body(env))).GetProperty("number").GetString().ShouldBe("FTR2026000000002");

        var rolled = await CreateAndIssue(env, Body(env, date: NextYear));

        rolled.GetProperty("number").GetString().ShouldBe("FTR2027000000001");
        var list = (await SeriesList(env)).EnumerateArray().ToList();
        list.Count.ShouldBe(2);
        var row = list.Single(s => s.GetProperty("year").GetInt32() == 2027);
        row.GetProperty("prefix").GetString().ShouldBe("FTR");
        row.GetProperty("nextPreview").GetString().ShouldBe("FTR2027000000002");
        row.GetProperty("isDefault").GetBoolean().ShouldBeFalse();
        list.Single(s => s.GetProperty("year").GetInt32() == 2026).GetProperty("isDefault").GetBoolean().ShouldBeTrue();

        // Eski yıla dönük kesim kendi sayacını sürdürür; yeni yıl sayacı etkilenmez.
        (await CreateAndIssue(env, Body(env))).GetProperty("number").GetString().ShouldBe("FTR2026000000003");
        (await CreateAndIssue(env, Body(env, date: new DateOnly(2025, 12, 30)))).GetProperty("number").GetString().ShouldBe("FTR2025000000001");
        (await CreateAndIssue(env, Body(env, date: NextYear))).GetProperty("number").GetString().ShouldBe("FTR2027000000002");
    }

    [Test]
    public async Task EBelge_YearRollover_CreatesSamePrefixSeries() {
        var env = await NewEnv();
        await EnableEBelge(env.Client);
        await CreateSeries(env, "XYZ", "EArchive");

        (await CreateAndIssue(env, Body(env))).GetProperty("number").GetString().ShouldBe("XYZ2026000000001");
        (await CreateAndIssue(env, Body(env, date: NextYear))).GetProperty("number").GetString().ShouldBe("XYZ2027000000001");
    }

    [Test]
    public async Task YearRollover_InParallel_CreatesTheNewYearSeriesOnce() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++) {
            var cari = await CreateCari(env.Client, "R" + i, customer: true, supplier: false);
            ids.Add((await Create(env, Body(env, date: NextYear, extra: new { currentAccountId = cari }))).GetProperty("id").GetGuid());
        }

        var responses = await IssueInParallel(env, ids);

        responses.ShouldAllBe(r => r.Status == HttpStatusCode.OK, string.Join(" | ", responses.Select(r => r.Body)));
        var numbers = new List<string>();
        foreach (var id in ids) numbers.Add(await NumberOf(env, id));
        numbers.Order().ToArray().ShouldBe(Enumerable.Range(1, 6).Select(n => $"FTR2027{n:D9}").ToArray());
        (await SeriesList(env)).EnumerateArray().Count(s => s.GetProperty("year").GetInt32() == 2027).ShouldBe(1);
    }

    [Test]
    public async Task Series_PaperPrefix_MustBeThreeUpperCaseAlnum() {
        var env = await NewEnv();
        foreach (var bad in new[] { "F", "FT", "FTR-", "ABCD", "ftr", "F R" }) {
            using var response = await env.Client.PostAsJsonAsync(SeriesUrl, new { branchId = env.Branch, kindGroup = "sales", mode = "Paper", prefix = bad, year = 2026 });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, bad);
        }

        (await CreateSeries(env, "A1B")).GetProperty("nextPreview").GetString().ShouldBe("A1B2026000000001");
    }

    // ---- I5: önek firmada tekil ---------------------------------------------------------------------------------

    [Test]
    public async Task Series_Prefix_IsUniquePerTenant_AcrossModeGroupAndBranch() {
        var env     = await NewEnv();
        var branch2 = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(env.Tenant, [new BranchRef(env.Branch, "MRK", "Merkez", true, true), new BranchRef(branch2, "SB2", "İkinci", true, false)]);
        await CreateSeries(env, "AAA", "EInvoice");

        async Task<HttpResponseMessage> Post(string group, string mode, Guid branch, int year = 2026)
            => await env.Client.PostAsJsonAsync(SeriesUrl, new { branchId = branch, kindGroup = group, mode, prefix = "AAA", year });

        foreach (var (group, mode, branch) in new[] { ("purchase", "EInvoice", env.Branch), ("sales", "EArchive", env.Branch), ("sales", "Paper", env.Branch), ("sales", "EInvoice", branch2) }) {
            using var response = await Post(group, mode, branch);
            await Fail(response, HttpStatusCode.Conflict, "prefix_taken");
        }

        using var sameYear = await Post("sales", "EInvoice", env.Branch);
        await Fail(sameYear, HttpStatusCode.Conflict, "series_exists");
        // Aynı serinin sonraki yılı serbest.
        using var nextYear = await Post("sales", "EInvoice", env.Branch, 2027);
        nextYear.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Güncellemede de aynı kural.
        var other = await CreateSeries(env, "BBB", "EInvoice", group: "purchase");
        using var update = await env.Client.PutAsJsonAsync($"{SeriesUrl}/{other.GetProperty("id").GetGuid()}",
                                                           new { kindGroup = "purchase", mode = "EInvoice", prefix = "AAA", isActive = true });
        await Fail(update, HttpStatusCode.Conflict, "prefix_taken");
    }

    [Test]
    public async Task Series_PrefixYear_IsBackedByAUniqueIndex() {
        var env = await NewEnv();
        await CreateSeries(env, "AAA", "EInvoice");
        await using var db = PricingTestSupport.Db(env.Tenant);
        db.DocumentSeries.Add(new DocumentSeries { KindGroup = "purchase", BranchId = env.Branch, Mode = InvoiceMode.Paper, Prefix = "AAA", Year = 2026 });

        var failure = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        failure.InnerException!.Message.ShouldContain("IX_document_series_prefix_year");
    }

    // ---- I6: varsayılan kâğıt serisi tembel açılır -------------------------------------------------------------

    [Test]
    public async Task Issue_WithoutSeries_ProvisionsDefaultPaperSeriesPerGroup() {
        var env = await NewEnv();

        var sales    = await CreateAndIssue(env, Body(env));
        var purchase = await CreateAndIssue(env, Body(env, "Purchase"));
        var again    = await CreateAndIssue(env, Body(env));

        // Şube varsayılan şube: sabit önekler SAT (satış) ve ALS (alış).
        sales.GetProperty("number").GetString().ShouldBe("SAT2026000000001");
        purchase.GetProperty("number").GetString().ShouldBe("ALS2026000000001");
        again.GetProperty("number").GetString().ShouldBe("SAT2026000000002");
        var list = (await SeriesList(env)).EnumerateArray().ToList();
        list.Count.ShouldBe(2);
        list.ShouldAllBe(s => s.GetProperty("isDefault").GetBoolean() && s.GetProperty("mode").GetString() == "paper");
    }

    [Test]
    public async Task Issue_WithoutSeries_NonMainBranchGetsDeterministicUniquePrefixes() {
        var env     = await NewEnv();
        var branch2 = Guid.CreateVersion7();
        var branch3 = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(env.Tenant, [new BranchRef(env.Branch, "MRK", "Merkez", true, true), new BranchRef(branch2, "SB2", "İkinci", true, false),
                                                           new BranchRef(branch3, "SB3", "Üçüncü", true, false)]);

        var a = await CreateAndIssue(env, Body(env, extra: new { branchId = branch2 }));
        var b = await CreateAndIssue(env, Body(env, extra: new { branchId = branch3 }));
        var c = await CreateAndIssue(env, Body(env));

        var prefixes = new[] { a, b, c }.Select(x => x.GetProperty("number").GetString()![..3]).ToArray();
        prefixes.Distinct().Count().ShouldBe(3);
        prefixes[2].ShouldBe("SAT");
        prefixes.ShouldAllBe(p => p.Length == 3 && p[0] == 'S');
        // Aynı şube ikinci kez aynı seriyi kullanır.
        (await CreateAndIssue(env, Body(env, extra: new { branchId = branch2 }))).GetProperty("number").GetString().ShouldBe(prefixes[0] + "2026000000002");
    }

    [Test]
    public async Task Issue_WithoutSeries_InParallel_ProvisionsOnce() {
        var env = await NewEnv();
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++) {
            var cari = await CreateCari(env.Client, "P" + i, customer: true, supplier: false);
            ids.Add((await Create(env, Body(env, extra: new { currentAccountId = cari }))).GetProperty("id").GetGuid());
        }

        var responses = await IssueInParallel(env, ids);

        responses.ShouldAllBe(r => r.Status == HttpStatusCode.OK, string.Join(" | ", responses.Select(r => r.Body)));
        (await SeriesList(env)).GetArrayLength().ShouldBe(1);
        var numbers = new List<string>();
        foreach (var id in ids) numbers.Add(await NumberOf(env, id));
        numbers.Order().ToArray().ShouldBe(Enumerable.Range(1, 6).Select(n => $"SAT2026{n:D9}").ToArray());
    }

    [Test]
    public async Task Issue_EBelgeWithoutSeries_StillReturnsSeriesMissing() {
        var env = await NewEnv();
        await EnableEBelge(env.Client);
        var draft = await Create(env, Body(env));

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "series_missing");
    }

    // ---- I1: iptal ve iade birbirini korur ----------------------------------------------------------------------

    private static async Task<(JsonElement Original, Guid LineId)> IssuedOriginal(Env env) {
        var original = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 10m, 10m)]));
        return (original, original.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    private static Dictionary<string, object?> ReturnBody(Env env, Guid originalId, Guid lineId, decimal quantity) {
        return Body(env, "SalesReturn", [ProductLine(env, quantity, 10m, new { sourceLineId = lineId })], new { returnOfInvoiceId = originalId });
    }

    [Test]
    public async Task Cancel_WithActiveReturn_Returns409_AndWorksAfterReturnIsCancelled() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var (original, lineId) = await IssuedOriginal(env);
        var originalId = original.GetProperty("id").GetGuid();
        var ret        = await CreateAndIssue(env, ReturnBody(env, originalId, lineId, 4m));

        using var blocked = await Cancel(env.Client, originalId);
        await Fail(blocked, HttpStatusCode.Conflict, "invoice_has_returns");
        (await (await env.Client.GetAsync($"{Url}/{originalId}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("issued");

        (await Cancel(env.Client, ret.GetProperty("id").GetGuid())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Cancel(env.Client, originalId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task IssueReturn_AgainstCancelledOriginal_Returns409() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var (original, lineId) = await IssuedOriginal(env);
        var originalId = original.GetProperty("id").GetGuid();
        var draft      = await Create(env, ReturnBody(env, originalId, lineId, 4m));
        (await Cancel(env.Client, originalId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "original_cancelled");
        (await (await env.Client.GetAsync($"{Url}/{draft.GetProperty("id").GetGuid()}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("draft");
    }

    [Test]
    public async Task CancelOriginalRacingReturnIssue_NeverLeavesAnOrphanReturn() {
        for (var round = 0; round < 3; round++) {
            var env = await NewEnv(productCode: "R" + round);
            await CreateSeries(env, "FTR");
            var (original, lineId) = await IssuedOriginal(env);
            var originalId = original.GetProperty("id").GetGuid();
            var draft      = (await Create(env, ReturnBody(env, originalId, lineId, 4m))).GetProperty("id").GetGuid();

            var codes = await Task.WhenAll(Task.Run(async () => {
                                                         using var r = await Cancel(env.Client, originalId);
                                                         return (int)r.StatusCode;
                                                     }),
                                           Task.Run(async () => {
                                                         using var r = await env.Client.PostAsync($"{Url}/{draft}/issue", null);
                                                         return (int)r.StatusCode;
                                                     }));

            // Ya iade kesilir ve iptal 409 olur ya da iptal olur ve iade 409 olur; ikisi birden olmaz.
            codes.Count(c => c is 200 or 204).ShouldBe(1, string.Join(",", codes));
            var originalStatus = (await (await env.Client.GetAsync($"{Url}/{originalId}")).JsonAsync()).GetProperty("status").GetString();
            var returnStatus   = (await (await env.Client.GetAsync($"{Url}/{draft}")).JsonAsync()).GetProperty("status").GetString();
            (originalStatus == "cancelled" && returnStatus == "issued").ShouldBeFalse();
        }
    }

    // ---- I2: seri/lot satırı ------------------------------------------------------------------------------------

    private static async Task<(Env Env, Guid Serial, Guid Lot, Guid Warehouse)> NewTrackedEnv() {
        var env    = await NewEnv(modules: ["seri-lot"]);
        var serial = await CreateProduct(env.Client, "SER-1", new { tracksSerialLot = true, trackingMode = "serial", vatRate = 20, baseUnit = "adet" });
        var lot    = await CreateProduct(env.Client, "LOT-1", new { tracksSerialLot = true, trackingMode = "lot", vatRate = 20, baseUnit = "adet" });
        return (env, serial, lot, await Warehouse(env));
    }

    [Test]
    public async Task TrackedItem_OnStockInvoiceWithoutSerialsOrLots_Returns400WithTurkishMessage() {
        var (env, serial, lot, warehouse) = await NewTrackedEnv();
        await CreateSeries(env, "FTR");

        foreach (var product in new[] { serial, lot }) {
            using var response = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 10m, product: product)], extra: new { affectsStock = true, warehouseId = warehouse }));
            await Fail(response, HttpStatusCode.BadRequest, "tracking_required");
            (await response.Content.ReadAsStringAsync()).ShouldContain("seri");
        }

        // Stoğu etkilemeyen fatura serisiz kesilebilir.
        var plain = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 1m, 10m, product: serial)]));
        plain.GetProperty("status").GetString().ShouldBe("issued");
    }

    [Test]
    public async Task TrackedItem_SerialsFlowFromPurchaseToSale() {
        var (env, serial, _, warehouse) = await NewTrackedEnv();
        await CreateSeries(env, "FTR");
        await CreateSeries(env, "ALF", group: "purchase");

        var purchase = await CreateAndIssue(env, Body(env, "Purchase", [ProductLine(env, 2m, 100m, new { serials = new[] { "SN-1", "SN-2" } }, serial)],
                                                      new { affectsStock = true, warehouseId = warehouse }));
        purchase.GetProperty("lines")[0].GetProperty("serials").GetArrayLength().ShouldBe(2);
        (await StockOf(env, warehouse, serial)).Physical.ShouldBe(2m);

        var sale = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 1m, 150m, new { serials = new[] { "SN-1" } }, serial)],
                                                  extra: new { affectsStock = true, warehouseId = warehouse }));
        sale.GetProperty("status").GetString().ShouldBe("issued");
        (await StockOf(env, warehouse, serial)).Physical.ShouldBe(1m);

        // Satılmış seri tekrar satılamaz.
        using var again = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 150m, new { serials = new[] { "SN-1" } }, serial)],
                                                                     extra: new { affectsStock = true, warehouseId = warehouse }));
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var issue = await env.Client.PostAsync($"{Url}/{(await again.JsonAsync()).GetProperty("id").GetGuid()}/issue", null);
        issue.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Test]
    public async Task TrackedItem_LotsFlowFromPurchase() {
        var (env, _, lot, warehouse) = await NewTrackedEnv();
        await CreateSeries(env, "ALF", group: "purchase");

        var purchase = await CreateAndIssue(env, Body(env, "Purchase",
                                                      [ProductLine(env, 5m, 10m, new { lots = new[] { new { lotNo = "L1", expiry = (string?)null, quantity = 5m, lotId = (Guid?)null } } }, lot)],
                                                      new { affectsStock = true, warehouseId = warehouse }));

        purchase.GetProperty("status").GetString().ShouldBe("issued");
        (await StockOf(env, warehouse, lot)).Physical.ShouldBe(5m);
    }

    [Test]
    public async Task TrackedItem_IssueRejectsDraftWhoseItemBecameTracked() {
        var env       = await NewEnv(modules: ["seri-lot"]);
        var warehouse = await Warehouse(env);
        await CreateSeries(env, "FTR");
        await Stock(env, warehouse, 5m);
        var draft = await Create(env, Body(env, lines: [ProductLine(env, 1m, 10m)], extra: new { affectsStock = true, warehouseId = warehouse }));
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var product = await db.Products.SingleAsync(p => p.Id == env.Product);
            product.TracksSerialLot = true;
            product.TrackingMode    = ERP.Domain.Inventory.TrackingMode.Serial;
            await db.SaveChangesAsync();
        }

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        await Fail(response, HttpStatusCode.BadRequest, "tracking_required");
    }

    // ---- I3: yazı tipi yedeği -----------------------------------------------------------------------------------

    [Test]
    public async Task Pdf_WithEmojiCjkArabicAndControlCharacters_StillRenders() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var exotic = "Çay 🍵 茶 شاي ‏ ​ \u0007 ǅ ☃";
        var issued = await CreateAndIssue(env, Body(env, lines: [FreeLine(exotic)], extra: new { notes = "Not 😀 日本語 \u0001" }));

        var pdf = await PdfTests.GetPdf(env.Client, issued.GetProperty("id").GetGuid());

        pdf.AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
        PdfTests.Text(pdf).ShouldContain("Çay");
        PdfTests.Text(pdf).ShouldContain("FTR2026000000001");
    }
}
