using System.Text.Json;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class DraftTests : TestBase {
    [Test]
    public async Task CreateDraft_ComputesTotals() {
        var env = await NewEnv();
        // 2 x 50 = 100, %10 satır iskontosu -> net 90, KDV %20 = 18. Hizmet: 200, KDV %10 = 20.
        var body = Body(env, lines: [ProductLine(env, 2m, 50m, new { discountPercent = 10m }), FreeLine()]);

        var draft = await Create(env, body);

        draft.GetProperty("status").GetString().ShouldBe("draft");
        draft.GetProperty("number").ValueKind.ShouldBe(JsonValueKind.Null);
        Dec(draft, "totals", "gross").ShouldBe(300m);
        Dec(draft, "totals", "discount").ShouldBe(10m);
        Dec(draft, "totals", "net").ShouldBe(290m);
        Dec(draft, "totals", "vat").ShouldBe(38m);
        Dec(draft, "totals", "payable").ShouldBe(328m);
        Dec(draft, "totalsTl", "payable").ShouldBe(328m);
        var line = draft.GetProperty("lines")[0];
        Dec(line, "net").ShouldBe(90m);
        Dec(line, "vat").ShouldBe(18m);
        line.GetProperty("description").GetString().ShouldBe("Ürün P1");
        line.GetProperty("unit").GetString().ShouldBe("adet");
        draft.GetProperty("vatSummary").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task CreateDraft_ForeignCurrency_StoresTlAtDocumentRate() {
        var env = await NewEnv();

        var draft = await Create(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], extra: new { currency = "USD", rate = 30.5m }));

        draft.GetProperty("rateOverridden").GetBoolean().ShouldBeTrue();
        Dec(draft, "totals", "payable").ShouldBe(120m);
        Dec(draft, "totalsTl", "net").ShouldBe(3050m);
        Dec(draft, "totalsTl", "vat").ShouldBe(610m);
        Dec(draft, "totalsTl", "payable").ShouldBe(3660m);
    }

    [Test]
    public async Task CreateDraft_ForeignCurrencyWithoutRate_409RateMissing() {
        var env = await NewEnv();

        using var response = await env.Client.PostAsJsonAsync(Url, Body(env, extra: new { currency = "EUR" }));

        await Fail(response, HttpStatusCode.Conflict, "rate_missing");
    }

    [Test]
    public async Task UpdateDraft_ReplacesLines_AndChecksVersion() {
        var env   = await NewEnv();
        var draft = await Create(env, Body(env));
        var id    = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetUInt32();

        var changed = Body(env, lines: [FreeLine("Tek satır", 3m, 100m, 20)], extra: new { version });
        using var ok = await env.Client.PutAsJsonAsync($"{Url}/{id}", changed);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var updated = await ok.JsonAsync();
        updated.GetProperty("lines").GetArrayLength().ShouldBe(1);
        Dec(updated, "totals", "payable").ShouldBe(360m);
        updated.GetProperty("version").GetUInt32().ShouldNotBe(version);

        // Eski sürümle yazmak çakışır.
        using var stale = await env.Client.PutAsJsonAsync($"{Url}/{id}", changed);
        await Fail(stale, HttpStatusCode.Conflict, "version_conflict");

        var read = await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync();
        read.GetProperty("lines").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task DeleteDraft_RemovesIt() {
        var env   = await NewEnv();
        var draft = await Create(env, Body(env));
        var id    = draft.GetProperty("id").GetGuid();

        using var delete = await env.Client.DeleteAsync($"{Url}/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await env.Client.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Preview_MatchesIssuedTotals() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        // KDV %20/%10/%1 karışık, bölünmeyen net değerler ve %7 belge iskontosu.
        var lines = new object[] {
            FreeLine("A", 3m, 33.33m, 20), FreeLine("B", 7m, 14.29m, 10), FreeLine("C", 11m, 9.07m, 1)
        };
        var body = Body(env, lines: lines, extra: new { docDiscountPercent = 7m });

        using var previewResponse = await env.Client.PostAsJsonAsync($"{Url}/preview", body);
        previewResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await previewResponse.Content.ReadAsStringAsync());
        var preview = await previewResponse.JsonAsync();
        var issued  = await CreateAndIssue(env, body);

        Dec(preview, "totals", "payable").ShouldBe(307.63m);
        foreach (var name in new[] { "gross", "discount", "net", "otv", "vat", "withheld", "payable" }) {
            Dec(preview, "totals", name).ShouldBe(Dec(issued, "totals", name), name);
            Dec(preview, "totalsTl", name).ShouldBe(Dec(issued, "totalsTl", name), name);
        }

        for (var i = 0; i < 3; i++) {
            Dec(preview.GetProperty("lines")[i], "net").ShouldBe(Dec(issued.GetProperty("lines")[i], "net"));
            Dec(preview.GetProperty("lines")[i], "vat").ShouldBe(Dec(issued.GetProperty("lines")[i], "vat"));
        }

        // Önizleme hiçbir şey kaydetmez.
        var list = await (await env.Client.GetAsync(Url)).JsonAsync();
        list.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task MeasureLine_RequiresDimensions() {
        var env     = await NewEnv();
        var measure = await CreateProduct(env.Client, "KUM", new { soldByMeasure = true, measureUnit = "m2", baseUnit = "m2" });

        using var missing = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 10m, product: measure)]));
        await Fail(missing, HttpStatusCode.BadRequest, "measure_dimensions_required");
        using var widthOnly = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 10m, new { width = 2m }, measure)]));
        await Fail(widthOnly, HttpStatusCode.BadRequest, "measure_dimensions_required");

        var draft = await Create(env, Body(env, lines: [ProductLine(env, 1m, 10m, new { width = 2m, length = 1.5m }, measure)]));
        var line  = draft.GetProperty("lines")[0];
        Dec(line, "quantity").ShouldBe(3m);
        Dec(line, "net").ShouldBe(30m);
        Dec(line, "width").ShouldBe(2m);
    }

    [Test]
    public async Task DiscountOverLimit_Returns403() {
        var env = await NewEnv(maxDiscount: 5m);

        using var over = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 100m, new { discountPercent = 10m })]));
        await Fail(over, HttpStatusCode.Forbidden, "discount_limit_exceeded");
        // Belge iskontosu da sayılır.
        using var doc = await env.Client.PostAsJsonAsync(Url, Body(env, extra: new { docDiscountPercent = 6m }));
        await Fail(doc, HttpStatusCode.Forbidden, "discount_limit_exceeded");

        using var ok = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [ProductLine(env, 1m, 100m, new { discountPercent = 5m })]));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task PurchaseInvoiceWithoutFiyatAlis_Returns403() {
        var env = await NewEnv();
        var purchase = await Create(env, Body(env, "Purchase"));
        using var limited = await OtherClient(env, "fatura.read", "fatura.manage", "fatura.issue");

        using var create = await limited.PostAsJsonAsync(Url, Body(env, "Purchase"));
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var read = await limited.GetAsync($"{Url}/{purchase.GetProperty("id").GetGuid()}");
        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var issue = await limited.PostAsync($"{Url}/{purchase.GetProperty("id").GetGuid()}/issue", null);
        issue.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var preview = await limited.PostAsJsonAsync($"{Url}/preview", Body(env, "Purchase"));
        preview.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Satış faturası serbest; liste alış faturalarını göstermez.
        await Create(env, Body(env));
        var list = await (await limited.GetAsync(Url)).JsonAsync();
        list.GetProperty("total").GetInt32().ShouldBe(1);
        list.GetProperty("items")[0].GetProperty("kind").GetString().ShouldBe("sales");
    }

    [Test]
    public async Task PurchaseExternalNumberDuplicate_Returns409() {
        var env = await NewEnv();
        var first = await Create(env, Body(env, "Purchase", extra: new { externalNumber = "ABC-1" }));

        using var duplicate = await env.Client.PostAsJsonAsync(Url, Body(env, "Purchase", extra: new { externalNumber = "ABC-1" }));
        await Fail(duplicate, HttpStatusCode.Conflict, "external_number_taken");

        using var missing = await env.Client.PostAsJsonAsync(Url, Body(env, "Purchase", extra: new { externalNumber = (string?)null }));
        await Fail(missing, HttpStatusCode.BadRequest, "external_number_required");

        // Başka tedarikçide aynı numara serbest; kendi taslağını güncellemek çakışmaz.
        var other = await CreateCari(env.Client, "T2", customer: false, supplier: true);
        using var elsewhere = await env.Client.PostAsJsonAsync(Url, Body(env, "Purchase", extra: new { externalNumber = "ABC-1", currentAccountId = other }));
        elsewhere.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var self = await env.Client.PutAsJsonAsync($"{Url}/{first.GetProperty("id").GetGuid()}", Body(env, "Purchase", extra: new { externalNumber = "ABC-1" }));
        self.StatusCode.ShouldBe(HttpStatusCode.OK, await self.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Validation_ExemptionWithholdingAndCariRole() {
        var env = await NewEnv();

        using var exemptWithVat = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [FreeLine(vatRate: 10, extra: new { exemptionCode = "301" })]));
        await Fail(exemptWithVat, HttpStatusCode.BadRequest, "exemption_requires_zero_vat");
        using var both = await env.Client.PostAsJsonAsync(Url, Body(env, lines: [FreeLine(vatRate: 0, extra: new { exemptionCode = "301", withholdingCode = "601" })]));
        await Fail(both, HttpStatusCode.BadRequest, "withholding_with_exemption");

        var exempt = await Create(env, Body(env, lines: [FreeLine(vatRate: 0, extra: new { exemptionCode = "301", exemptionReason = "İhracat" })]));
        Dec(exempt, "totals", "vat").ShouldBe(0m);
        exempt.GetProperty("lines")[0].GetProperty("exemptionCode").GetString().ShouldBe("301");

        // Tedarikçi olan cariye satış faturası kesilemez.
        using var wrongRole = await env.Client.PostAsJsonAsync(Url, Body(env, extra: new { currentAccountId = env.Supplier }));
        await Fail(wrongRole, HttpStatusCode.BadRequest, "cari_not_customer");
    }

    [Test]
    public async Task DueSchedule_MustEqualPayable() {
        var env = await NewEnv();
        var lines = new object[] { FreeLine(price: 100m, vatRate: 20) };

        using var wrong = await env.Client.PostAsJsonAsync(Url, Body(env, lines: lines, extra: new {
            dueSchedule = new[] { new { dueDate = "2026-04-10", amount = 60m }, new { dueDate = "2026-05-10", amount = 50m } }
        }));
        await Fail(wrong, HttpStatusCode.BadRequest, "due_schedule_mismatch");

        var ok = await Create(env, Body(env, lines: lines, extra: new {
            dueSchedule = new[] { new { dueDate = "2026-05-10", amount = 60m }, new { dueDate = "2026-04-10", amount = 60m } }
        }));
        var due = ok.GetProperty("dueSchedule");
        due.GetArrayLength().ShouldBe(2);
        due[0].GetProperty("dueDate").GetString().ShouldBe("2026-04-10");
    }
}
