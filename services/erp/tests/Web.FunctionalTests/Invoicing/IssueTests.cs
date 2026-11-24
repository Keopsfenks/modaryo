using System.Text.Json;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class IssueTests : TestBase {
    [Test]
    public async Task Issue_AssignsEBelgeNumberFormat() {
        var env = await NewEnv();
        await EnableEBelge(env.Client, "commercial");
        await CreateSeries(env, "ABC", "EInvoice");
        var taxpayer = await CreateCari(env.Client, "M2", customer: true, supplier: false, taxpayer: true);

        var issued = await CreateAndIssue(env, Body(env, extra: new { currentAccountId = taxpayer }));

        issued.GetProperty("number").GetString().ShouldBe("ABC2026000000001");
        issued.GetProperty("number").GetString()!.Length.ShouldBe(16);
        issued.GetProperty("status").GetString().ShouldBe("issued");
        issued.GetProperty("mode").GetString().ShouldBe("eInvoice");
        issued.GetProperty("scenario").GetString().ShouldBe("commercial");
        issued.GetProperty("seriesId").ValueKind.ShouldNotBe(JsonValueKind.Null);
        issued.GetProperty("buyerOrSeller").GetProperty("title").GetString().ShouldBe("Cari M2");

        var second = await CreateAndIssue(env, Body(env, extra: new { currentAccountId = taxpayer }));
        second.GetProperty("number").GetString().ShouldBe("ABC2026000000002");
    }

    [Test]
    public async Task Issue_PaperSeriesNumber() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR-", minWidth: 8);

        var issued = await CreateAndIssue(env, Body(env));

        issued.GetProperty("number").GetString().ShouldBe("FTR-00000001");
        issued.GetProperty("mode").GetString().ShouldBe("paper");
        issued.GetProperty("scenario").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Issue_EBelgeEnabledNonTaxpayer_UsesEArchive() {
        var env = await NewEnv();
        await EnableEBelge(env.Client);
        await CreateSeries(env, "ABC", "EInvoice");
        await CreateSeries(env, "XYZ", "EArchive");

        var issued = await CreateAndIssue(env, Body(env));

        issued.GetProperty("number").GetString().ShouldBe("XYZ2026000000001");
        issued.GetProperty("mode").GetString().ShouldBe("eArchive");
        issued.GetProperty("scenario").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Issue_PurchaseUsesPurchaseSeries_AndKeepsSupplierNumber() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        await CreateSeries(env, "AF", group: "purchase");

        var issued = await CreateAndIssue(env, Body(env, "Purchase", extra: new { externalNumber = "TED-77" }));

        issued.GetProperty("number").GetString().ShouldBe("AF000001");
        issued.GetProperty("externalNumber").GetString().ShouldBe("TED-77");
    }

    [Test]
    public async Task Issue_NoSeries_Returns409SeriesMissing() {
        var env   = await NewEnv();
        var draft = await Create(env, Body(env));

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "series_missing");
        (await (await env.Client.GetAsync($"{Url}/{draft.GetProperty("id").GetGuid()}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("draft");
    }

    [Test]
    public async Task Issue_SeriesOfAnotherYear_FallsBackToSamePrefixOfThatYear_ElseMissing() {
        var env = await NewEnv();
        await CreateSeries(env, "F", year: 2025);

        var draft = await Create(env, Body(env));
        using var missing = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);
        await Fail(missing, HttpStatusCode.Conflict, "series_missing");

        await CreateSeries(env, "F", year: 2026);
        var issued = await Issue(env.Client, draft.GetProperty("id").GetGuid());
        issued.GetProperty("number").GetString().ShouldBe("F000001");
    }

    [Test]
    public async Task UpdateIssued_Returns409NotDraft() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var issued = await CreateAndIssue(env, Body(env));
        var id     = issued.GetProperty("id").GetGuid();

        using var update = await env.Client.PutAsJsonAsync($"{Url}/{id}", Body(env));
        await Fail(update, HttpStatusCode.Conflict, "invoice_not_draft");
        using var delete = await env.Client.DeleteAsync($"{Url}/{id}");
        await Fail(delete, HttpStatusCode.Conflict, "invoice_not_draft");
        using var again = await env.Client.PostAsync($"{Url}/{id}/issue", null);
        await Fail(again, HttpStatusCode.Conflict, "invoice_not_draft");
    }

    [Test]
    public async Task IssueInLockedPeriod_Returns409() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        await LockPeriod(env.Client, "2026-03-31");
        var draft = await Create(env, Body(env));

        using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        await Fail(response, HttpStatusCode.Conflict, "period_locked");
        // Numara harcanmadı.
        var series = await (await env.Client.GetAsync(SeriesUrl)).JsonAsync();
        series[0].GetProperty("nextPreview").GetString().ShouldBe("F000001");
    }

    [Test]
    public async Task Issue_RequiresIssuePermission_AndActiveCari() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var draft = await Create(env, Body(env));
        var id    = draft.GetProperty("id").GetGuid();
        using var manager = await OtherClient(env, "fatura.read", "fatura.manage");

        using var forbidden = await manager.PostAsync($"{Url}/{id}/issue", null);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var deactivate = await env.Client.PostAsync($"/api/erp/current-accounts/{env.Customer}/deactivate", null);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deactivate.Content.ReadAsStringAsync());
        using var inactive = await env.Client.PostAsync($"{Url}/{id}/issue", null);
        await Fail(inactive, HttpStatusCode.Conflict, "current_account_inactive");
    }

    [Test]
    public async Task Return_ExceedsOriginal_Returns400() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var original = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 5m, 10m)]));
        var lineId   = original.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var originalId = original.GetProperty("id").GetGuid();

        object[] Return(decimal quantity) => [ProductLine(env, quantity, 10m, new { sourceLineId = lineId })];

        // Asıl faturadan fazlası zaten taslakta reddedilir.
        using var tooMany = await env.Client.PostAsJsonAsync(Url, Body(env, "SalesReturn", Return(6m), new { returnOfInvoiceId = originalId }));
        await Fail(tooMany, HttpStatusCode.BadRequest, "return_exceeds_original");

        var first = await CreateAndIssue(env, Body(env, "SalesReturn", Return(3m), new { returnOfInvoiceId = originalId }));
        first.GetProperty("status").GetString().ShouldBe("issued");

        // Kalan 2: 3 daha iade edilemez.
        using var second = await env.Client.PostAsJsonAsync(Url, Body(env, "SalesReturn", Return(3m), new { returnOfInvoiceId = originalId }));
        await Fail(second, HttpStatusCode.BadRequest, "return_exceeds_original");
        using var rest = await env.Client.PostAsJsonAsync(Url, Body(env, "SalesReturn", Return(2m), new { returnOfInvoiceId = originalId }));
        rest.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Asıl fatura ve satır bağı zorunlu.
        using var noOriginal = await env.Client.PostAsJsonAsync(Url, Body(env, "SalesReturn", Return(1m)));
        await Fail(noOriginal, HttpStatusCode.BadRequest, "return_of_required");
        using var noLine = await env.Client.PostAsJsonAsync(Url, Body(env, "SalesReturn", [ProductLine(env, 1m, 10m)], new { returnOfInvoiceId = originalId }));
        await Fail(noLine, HttpStatusCode.BadRequest, "return_line_required");
    }

    [Test]
    public async Task Return_ConcurrentDraftsCannotBothIssueBeyondOriginal() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var original   = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 5m, 10m)]));
        var lineId     = original.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var originalId = original.GetProperty("id").GetGuid();
        object[] Return(decimal quantity) => [ProductLine(env, quantity, 10m, new { sourceLineId = lineId })];
        var a = await Create(env, Body(env, "SalesReturn", Return(4m), new { returnOfInvoiceId = originalId }));
        var b = await Create(env, Body(env, "SalesReturn", Return(4m), new { returnOfInvoiceId = originalId }));

        var results = await Task.WhenAll(new[] { a, b }.Select(draft => Task.Run(async () => {
                                                                                      using var response = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);
                                                                                      return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : await response.ErrorCodeAsync());
                                                                                  })));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Single(r => r.StatusCode != HttpStatusCode.OK).ShouldBe((HttpStatusCode.BadRequest, "return_exceeds_original"));
    }
}
