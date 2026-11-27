using System.Text.Json;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using ERP.Infrastructure.Sales;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class RecurringTests : TestBase {
    private static readonly string[] RecurringPermissions = [.. OrderPermissions, "ayarlar.fiyat"];

    private static RecurringInvoiceJob Job => TestEnvironment.Factory.Services.GetRequiredService<RecurringInvoiceJob>();

    private static object RecLine(decimal price = 100m, decimal quantity = 1m, bool fixedPrice = true, Guid? product = null, bool vatIncluded = false, decimal? discount = null) {
        return new {
            productId = product, description = "Aylık bakım", unit = "adet", quantity, unitPrice = price, vatIncluded, vatRate = 20, discountPercent = discount, fixedPrice
        };
    }

    private static Dictionary<string, object?> TemplateBody(Env env, object? extra = null, object[]? lines = null) {
        var body = new Dictionary<string, object?> {
            ["name"] = "Bakım aboneliği", ["currentAccountId"] = env.Customer, ["branchId"] = env.Branch, ["currency"] = "TRY", ["interval"] = "monthly", ["dayOfMonth"] = 10,
            ["startDate"] = "2026-03-10", ["mode"] = "draft", ["lines"] = lines ?? [RecLine()]
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    private static async Task<JsonElement> CreateTemplate(Env env, object? extra = null, object[]? lines = null) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/recurring", TemplateBody(env, extra, lines));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<List<JsonElement>> Runs(Env env, Guid template) {
        using var response = await env.Client.GetAsync($"{Api}/recurring/{template}/runs");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    private static async Task<List<Invoice>> RecurringInvoices(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        var ids = await db.RecurringRuns.AsNoTracking().Select(r => r.InvoiceId).ToListAsync();
        return await db.Invoices.AsNoTracking().Include(i => i.Lines).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Date).ToListAsync();
    }

    [Test]
    public async Task Create_NumbersTemplateAndValidates() {
        var env = await NewOrderEnv(RecurringPermissions);

        var first  = await CreateTemplate(env);
        var second = await CreateTemplate(env);

        first.GetProperty("number").GetString().ShouldBe("AB0001");
        second.GetProperty("number").GetString().ShouldBe("AB0002");
        first.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        first.GetProperty("total").GetDecimal().ShouldBe(120m);
        using var badDay = await env.Client.PostAsJsonAsync($"{Api}/recurring", TemplateBody(env, new { dayOfMonth = 29 }));
        badDay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var badEnd = await env.Client.PostAsJsonAsync($"{Api}/recurring", TemplateBody(env, new { endDate = "2026-01-01" }));
        badEnd.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var noLines = await env.Client.PostAsJsonAsync($"{Api}/recurring", TemplateBody(env, null, []));
        noLines.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Recurring_RunTwiceSameDay_OneInvoice() {
        var env      = await NewOrderEnv(RecurringPermissions);
        var template = (await CreateTemplate(env)).GetProperty("id").GetGuid();
        var day      = new DateOnly(2026, 3, 10);

        var first  = await Job.RunOnceAsync(day, env.Tenant);
        var second = await Job.RunOnceAsync(day, env.Tenant);

        first.ShouldBe(1);
        second.ShouldBe(0);
        var invoice = (await RecurringInvoices(env)).ShouldHaveSingleItem();
        invoice.Date.ShouldBe(day);
        invoice.Payable.ShouldBe(120m);
        invoice.Status.ShouldBe(InvoiceStatus.Draft);
        invoice.SourceType.ShouldBe("recurring");
        var runs = await Runs(env, template);
        runs.ShouldHaveSingleItem().GetProperty("period").GetString().ShouldBe("2026-03-10");
    }

    [Test]
    public async Task Recurring_RunParallel_OneInvoice() {
        var env = await NewOrderEnv(RecurringPermissions);
        await CreateTemplate(env, new { mode = "autoIssue" });
        var day = new DateOnly(2026, 3, 10);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Job.RunOnceAsync(day, env.Tenant))));

        results.Sum().ShouldBe(1);
        var invoice = (await RecurringInvoices(env)).ShouldHaveSingleItem();
        invoice.Status.ShouldBe(InvoiceStatus.Issued);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.RecurringRuns.CountAsync()).ShouldBe(1);
        (await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Issued)).ShouldBe(1);
    }

    [Test]
    public async Task Recurring_AutoIssueIssues_DraftModeLeavesDraft() {
        var env   = await NewOrderEnv(RecurringPermissions);
        var auto  = (await CreateTemplate(env, new { name = "Otomatik", mode = "autoIssue" })).GetProperty("id").GetGuid();
        var draft = (await CreateTemplate(env, new { name = "Taslak", mode = "draft" })).GetProperty("id").GetGuid();

        (await Job.RunOnceAsync(new DateOnly(2026, 3, 10), env.Tenant)).ShouldBe(2);

        var invoices = await RecurringInvoices(env);
        invoices.Count.ShouldBe(2);
        var runs = (await Runs(env, auto)).Concat(await Runs(env, draft)).ToList();
        runs.Count.ShouldBe(2);
        var issued = invoices.Single(i => i.Status == InvoiceStatus.Issued);
        issued.Number.ShouldNotBeNullOrEmpty();
        invoices.Single(i => i.Status == InvoiceStatus.Draft).Number.ShouldBeNull();
        (await Runs(env, auto)).Single().GetProperty("invoiceId").GetGuid().ShouldBe(issued.Id);
        using var due = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}/due-items");
        (await due.JsonAsync()).EnumerateArray().Sum(i => i.GetProperty("remaining").GetDecimal()).ShouldBe(120m);
    }

    [Test]
    public async Task Recurring_RequotesNonFixedLines() {
        var env     = await NewOrderEnv(RecurringPermissions);
        var product = await PricingApi.CreateProduct(env.Client, "ABN", new { vatRate = 20, baseUnit = "adet" });
        var list    = await PricingApi.CreateList(env.Client, "Abonelik");
        (await PricingApi.PutRows(env.Client, list, PricingApi.Row(product, PricingApi.Tiers((1, 100m)), unit: "adet"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await PricingApi.SetSettings(env.Client, 0, new { defaultSaleListId = list });
        await CreateTemplate(env, null, [
            RecLine(80m, 1m, false, product, true),           // sabit olmayan: liste fiyatıyla yeniden fiyatlanır
            RecLine(50m, 1m, true, null, false)               // sabit
        ]);

        await Job.RunOnceAsync(new DateOnly(2026, 3, 10), env.Tenant);
        var rows = (await PricingApi.Items(env.Client, list)).Single();
        (await PricingApi.PutRows(env.Client, list, PricingApi.Row(product, PricingApi.Tiers((1, 110m)), rows.GetProperty("version").GetUInt32(), "adet"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Job.RunOnceAsync(new DateOnly(2026, 4, 10), env.Tenant);

        var invoices = await RecurringInvoices(env);
        invoices.Count.ShouldBe(2);
        // 100 KDV dahil + 50 KDV hariç (60 brüt) = 160; sonra 110 + 60 = 170. Sabit satır değişmez.
        invoices[0].Payable.ShouldBe(160m);
        invoices[1].Payable.ShouldBe(170m);
        invoices[1].Lines.Single(l => l.ProductId == null).UnitPrice.ShouldBe(50m);
    }

    [Test]
    public async Task Recurring_EndDateStops() {
        var env      = await NewOrderEnv(RecurringPermissions);
        var template = (await CreateTemplate(env, new { endDate = "2026-04-30" })).GetProperty("id").GetGuid();

        (await Job.RunOnceAsync(new DateOnly(2026, 3, 10), env.Tenant)).ShouldBe(1);
        (await Job.RunOnceAsync(new DateOnly(2026, 4, 10), env.Tenant)).ShouldBe(1);
        (await Job.RunOnceAsync(new DateOnly(2026, 5, 10), env.Tenant)).ShouldBe(0);
        (await Job.RunOnceAsync(new DateOnly(2026, 9, 10), env.Tenant)).ShouldBe(0);

        (await Runs(env, template)).Select(r => r.GetProperty("period").GetString()).ShouldBe(["2026-03-10", "2026-04-10"], ignoreOrder: true);
    }

    [Test]
    public async Task Recurring_MissedDays_AreCaughtUpOnNextRun_AndNothingBeforeStart() {
        var env      = await NewOrderEnv(RecurringPermissions);
        var template = (await CreateTemplate(env, new { interval = "quarterly" })).GetProperty("id").GetGuid();

        (await Job.RunOnceAsync(new DateOnly(2026, 3, 9), env.Tenant)).ShouldBe(0); // başlangıçtan önce
        (await Job.RunOnceAsync(new DateOnly(2026, 12, 20), env.Tenant)).ShouldBe(4); // 3/10, 6/10, 9/10, 12/10

        (await Runs(env, template)).Count.ShouldBe(4);
    }

    [Test]
    public async Task Recurring_PausedTemplate_IsSkippedUntilResumed() {
        var env      = await NewOrderEnv(RecurringPermissions);
        var template = (await CreateTemplate(env)).GetProperty("id").GetGuid();
        (await Post(env.Client, $"recurring/{template}/pause")).GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await Job.RunOnceAsync(new DateOnly(2026, 3, 10), env.Tenant)).ShouldBe(0);

        (await Post(env.Client, $"recurring/{template}/resume")).GetProperty("isActive").GetBoolean().ShouldBeTrue();
        (await Job.RunOnceAsync(new DateOnly(2026, 3, 10), env.Tenant)).ShouldBe(1);
    }

    [Test]
    public async Task Recurring_DiscountAboveUserLimit_IsRejectedAtSave_ButGeneratesForAllowedDiscount() {
        var env = await NewSalesEnv(RecurringPermissions, 10m);

        using var response = await env.Client.PostAsJsonAsync($"{Api}/recurring", TemplateBody(env, null, [RecLine(100m, 1m, true, null, false, 15m)]));

        await Fail(response, HttpStatusCode.Forbidden, "discount_limit_exceeded");
        var capped = await NewSalesEnv(RecurringPermissions, 20m);
        await CreateTemplate(capped, null, [RecLine(100m, 1m, true, null, false, 15m)]);
        (await Job.RunOnceAsync(new DateOnly(2026, 3, 10), capped.Tenant)).ShouldBe(1); // arka plan işi iskonto sınırına takılmaz
        (await RecurringInvoices(capped)).Single().Payable.ShouldBe(102m); // 85 + KDV
    }

    [Test]
    public async Task CashFlow_IncludesRecurringForecast() {
        var env      = await NewOrderEnv(RecurringPermissions);
        var template = (await CreateTemplate(env, new { startDate = "2026-04-10" })).GetProperty("id").GetGuid();

        using var response = await env.Client.GetAsync($"{TreasuryTestSupport.Base}/reports/cash-flow?granularity=week&periods=3&from=2026-04-01");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var periods = (await response.JsonAsync()).GetProperty("periods").EnumerateArray().ToList();

        // 4/10 haftası: 4/8-4/14 (ikinci hafta).
        periods[1].GetProperty("inflow").GetProperty("recurring").GetDecimal().ShouldBe(120m);
        periods[1].GetProperty("inflow").GetProperty("total").GetDecimal().ShouldBe(120m);
        periods[0].GetProperty("inflow").GetProperty("recurring").GetDecimal().ShouldBe(0m);
        periods[2].GetProperty("inflow").GetProperty("recurring").GetDecimal().ShouldBe(0m);

        // Üretilen dönem artık tahmin değil, fatura vadesidir: çift sayılmaz.
        await Job.RunOnceAsync(new DateOnly(2026, 4, 10), env.Tenant);
        using var after = await env.Client.GetAsync($"{TreasuryTestSupport.Base}/reports/cash-flow?granularity=week&periods=3&from=2026-04-01");
        var next = (await after.JsonAsync()).GetProperty("periods").EnumerateArray().ToList();
        next[1].GetProperty("inflow").GetProperty("recurring").GetDecimal().ShouldBe(0m);

        // Duraklatılan şablon tahmine girmez.
        await Post(env.Client, $"recurring/{template}/pause");
        using var paused = await env.Client.GetAsync($"{TreasuryTestSupport.Base}/reports/cash-flow?granularity=week&periods=8&from=2026-04-01");
        (await paused.JsonAsync()).GetProperty("periods").EnumerateArray().ShouldAllBe(p => p.GetProperty("inflow").GetProperty("recurring").GetDecimal() == 0m);
    }
}
