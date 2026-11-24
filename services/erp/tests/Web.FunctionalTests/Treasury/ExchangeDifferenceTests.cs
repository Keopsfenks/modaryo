using System.Text.Json;
using ERP.Domain.Invoicing;
using ERP.Domain.Treasury;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class ExchangeDifferenceTests : TestBase {
    private const string Url = $"{Base}/exchange-differences";

    /// <summary>100 USD'lik (KDV'siz) fatura invoiceRate kuruyla kesilir, 100 USD paymentRate kuruyla tahsil edilir (otomatik tahsis).</summary>
    private static async Task<Env> Scenario(decimal invoiceRate, decimal paymentRate) {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 0)], extra: new { currency = "USD", rate = invoiceRate }, date: new DateOnly(2026, 3, 1)));
        using var response = await Collect(env, usd, 100m, method: "transfer", extra: new { rate = paymentRate });
        await Created(response);
        return env;
    }

    private static async Task<List<JsonElement>> List(Env env, string query = "") {
        using var response = await env.Client.GetAsync($"{Url}{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    private static Guid[] Ids(JsonElement item) {
        return item.GetProperty("allocationIds").EnumerateArray().Select(i => i.GetGuid()).ToArray();
    }

    [Test]
    public async Task KurFarki_ComputedOnceAndSettled() {
        var env = await Scenario(32m, 32.5m);

        var differences = await List(env);
        var item        = differences.Single();
        (item.GetProperty("foreignAmount").GetDecimal(), item.GetProperty("currency").GetString()).ShouldBe((100m, "USD"));
        (item.GetProperty("invoiceRate").GetDecimal(), item.GetProperty("paymentRate").GetDecimal(), item.GetProperty("difference").GetDecimal()).ShouldBe((32m, 32.5m, 50m));
        item.GetProperty("invoiceNumber").GetString().ShouldNotBeNullOrEmpty();

        using var first = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        await Created(first);

        // İşlenen fark artık listelenmez ve ikinci kez işlenemez.
        (await List(env)).ShouldBeEmpty();
        using var second = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        await Fail(second, HttpStatusCode.Conflict, "difference_already_settled");
        using var asExpense = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        await Fail(asExpense, HttpStatusCode.Conflict, "difference_already_settled");

        await using var db = PricingTestSupport.Db(env.Tenant);
        var settlement = await db.ExchangeDifferenceSettlements.AsNoTracking().SingleAsync();
        (settlement.Amount, settlement.ResultType, settlement.AllocationIds.Count).ShouldBe((50m, "Invoice", 1));
    }

    [Test]
    public async Task KurFarki_ParallelSettlement_OnlyOneWins() {
        var env  = await Scenario(32m, 32.5m);
        var ids  = Ids((await List(env)).Single());
        var body = new { allocationIds = ids };

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => {
            using var response = await env.Client.PostAsJsonAsync($"{Url}/invoice", body);
            return response.StatusCode;
        }));

        results.Count(r => r == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r == HttpStatusCode.Conflict).ShouldBe(4);
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.Invoices.AsNoTracking().CountAsync(i => i.Kind == InvoiceKind.ExchangeDifference)).ShouldBe(1);
    }

    [Test]
    public async Task SettledDifference_StaysSettledAfterAnotherCollection() {
        var env  = await Scenario(32m, 32.5m);
        var cash = await Account(env, "Kasa");
        using var settle = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids((await List(env)).Single()) });
        await Created(settle);

        // Aynı cariye başka bir tahsilat/fatura eklenip eşleştirmeler yeniden kurulsa da işlenen fark yeniden çıkmaz.
        await CollectOk(env, cash, 10m);
        await Invoice(env, 120m);
        (await List(env)).ShouldBeEmpty();
    }

    [Test]
    public async Task PositiveCreatesInvoiceDraftWithVat() {
        var env  = await Scenario(32m, 32.5m);
        var item = (await List(env)).Single();

        using var response = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item), vatRate = 10 });
        var       body     = await Created(response);
        body.GetProperty("amount").GetDecimal().ShouldBe(50m);

        await using var db      = PricingTestSupport.Db(env.Tenant);
        var             invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.Kind == InvoiceKind.ExchangeDifference);
        invoice.Id.ShouldBe(body.GetProperty("invoiceId").GetGuid());
        (invoice.Status, invoice.CurrentAccountId, invoice.Currency, invoice.SourceType).ShouldBe((InvoiceStatus.Draft, env.Customer, "TRY", "kur-farki"));
        // Tutar KDV dahildir: 50 = 45,45 + 4,55 (%10).
        (invoice.Payable, invoice.Net, invoice.Vat).ShouldBe((50m, 45.45m, 4.55m));
        invoice.Lines.Single().VatRate.ShouldBe(10);

        // Varsayılan KDV oranı %20.
        var defaults = await Scenario(32m, 33m);
        var other    = (await List(defaults)).Single();
        other.GetProperty("difference").GetDecimal().ShouldBe(100m);
        using var response2 = await defaults.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(other) });
        await Created(response2);
        await using var db2 = PricingTestSupport.Db(defaults.Tenant);
        var             draft = await db2.Invoices.AsNoTracking().SingleAsync(i => i.Kind == InvoiceKind.ExchangeDifference);
        (draft.Payable, draft.Vat).ShouldBe((100m, 16.67m));
    }

    [Test]
    public async Task NegativeCreatesExpense() {
        var env  = await Scenario(32m, 31.5m);
        var item = (await List(env)).Single();
        item.GetProperty("difference").GetDecimal().ShouldBe(-50m);

        // Negatif fark için fatura kesilemez.
        using var invoice = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        await Fail(invoice, HttpStatusCode.BadRequest, "difference_not_applicable");

        using var response = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        var       body     = await Created(response);
        body.GetProperty("amount").GetDecimal().ShouldBe(50m);

        // Gider fişi cariyi alacaklandırarak artakalan 50 TL borcu kapatır.
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        var document = (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Expense);
        (document.Amount, document.CurrentAccountId).ShouldBe((50m, env.Customer));
        await using var db       = PricingTestSupport.Db(env.Tenant);
        var             category = await db.ExpenseCategories.AsNoTracking().SingleAsync(c => c.Id == document.ExpenseCategoryId);
        category.Code.ShouldBe("kur-farki");
        (await db.ExchangeDifferenceSettlements.AsNoTracking().SingleAsync()).ResultType.ShouldBe("Expense");
        (await List(env)).ShouldBeEmpty();
    }

    [Test]
    public async Task NegativeExpenseFromTryAccount_MovesAccount() {
        var env  = await Scenario(32m, 31.5m);
        var cash = await Account(env, "Kasa");
        var item = (await List(env)).Single();

        using var response = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item), accountId = cash });
        await Created(response);

        (await AccountBalance(env, cash)).ShouldBe(-50m);
        (await CariBalance(env, env.Customer)).ShouldBe(50m);
    }

    [Test]
    public async Task TryOnlyEntries_NoDifferences() {
        var env  = await NewEnv();
        var cash = await Account(env, "Kasa");
        await Invoice(env, 1200m);
        await CollectOk(env, cash, 1200m);

        (await List(env)).ShouldBeEmpty();
        using var response = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = new[] { Guid.NewGuid() } });
        await Fail(response, HttpStatusCode.BadRequest, "difference_not_found");
    }

    [Test]
    public async Task SameRates_NoDifference_AndDateRangeFilters() {
        var env = await Scenario(32m, 32m);
        (await List(env)).ShouldBeEmpty();

        var dated = await Scenario(32m, 33m);
        (await List(dated, "?from=2026-03-01&to=2026-03-31")).Count.ShouldBe(1);
        (await List(dated, "?from=2026-04-01&to=2026-04-30")).ShouldBeEmpty();
        (await List(dated, $"?currentAccountId={Guid.NewGuid()}")).ShouldBeEmpty();
    }

    [Test]
    public async Task PartialPaymentAtHigherRate_UsesPaidForeignAmount() {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 0)], extra: new { currency = "USD", rate = 32m }, date: new DateOnly(2026, 3, 1)));
        using var response = await Collect(env, usd, 50m, method: "transfer", extra: new { rate = 32.5m });
        await Created(response);

        var item = (await List(env)).Single();
        (item.GetProperty("foreignAmount").GetDecimal(), item.GetProperty("difference").GetDecimal()).ShouldBe((50m, 25m));
    }

    [Test]
    public async Task Permissions_ReadNeedsFinansRead_InvoiceNeedsManage() {
        var env      = await NewEnv();
        var readOnly = await OtherClient(env, "finans.read");
        using (var ok = await readOnly.GetAsync(Url)) ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var denied = await readOnly.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = new[] { Guid.NewGuid() } })) denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var deniedExpense = await readOnly.PostAsJsonAsync($"{Url}/expense", new { allocationIds = new[] { Guid.NewGuid() } })) deniedExpense.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
