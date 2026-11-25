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
    public async Task NegativeExpenseWithAccountId_IsRejected_AndNothingMoves() {
        var env  = await Scenario(32m, 31.5m);
        var cash = await Account(env, "Kasa");
        var item = (await List(env)).Single();

        // Hiçbir para hareketi olmadığından kasa/banka hesabı seçilemez; artakalan cari borcu açık kalmaz.
        using var rejected = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item), accountId = cash });
        await Fail(rejected, HttpStatusCode.BadRequest, "difference_not_applicable");
        (await AccountBalance(env, cash)).ShouldBe(0m);
        (await CariBalance(env, env.Customer)).ShouldBe(50m);
        (await List(env)).Count.ShouldBe(1);

        using var accepted = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        await Created(accepted);
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        (await AccountBalance(env, cash)).ShouldBe(0m);
    }

    /// <summary>Tedarikçi tarafı: 100 USD'lik alış faturası invoiceRate kuruyla kesilir, 100 USD paymentRate kuruyla ödenir.</summary>
    private static async Task<Env> PayableScenario(decimal invoiceRate, decimal paymentRate) {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await CreateAndIssue(env, Body(env, "Purchase", [FreeLine(price: 100m, vatRate: 0)], new { currency = "USD", rate = invoiceRate, currentAccountId = env.Supplier }, new DateOnly(2026, 3, 1)));
        using var response = await Pay(env, usd, 100m, method: "transfer", extra: new { rate = paymentRate });
        await Created(response);
        return env;
    }

    [Test]
    public async Task PayablePositiveDifference_NoSalesInvoice_GelirClosesSupplier() {
        // Alış 32,50'den yazıldı, 32,00'den ödendi: tedarikçide 50 TL alacak artığı kalır (borç değil, kâr).
        var env  = await PayableScenario(32.5m, 32m);
        var item = (await List(env)).Single();
        (item.GetProperty("side").GetString(), item.GetProperty("difference").GetDecimal()).ShouldBe(("payable", 50m));
        var before = await CariBalance(env, env.Supplier);
        before.ShouldNotBe(0m);

        // Satış tipi kur farkı faturası kesilemez (KDV ve alacak yanlış yönde olurdu).
        using var invoice = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        await Fail(invoice, HttpStatusCode.BadRequest, "difference_not_applicable");
        await using (var check = PricingTestSupport.Db(env.Tenant)) (await check.Invoices.AsNoTracking().CountAsync(i => i.Kind == InvoiceKind.ExchangeDifference)).ShouldBe(0);

        // Kur farkı geliri fişi tedarikçiyi borçlandırır ve artığı kapatır.
        using var response = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        var       body     = await Created(response);
        body.GetProperty("amount").GetDecimal().ShouldBe(50m);
        (await CariBalance(env, env.Supplier)).ShouldBe(0m);

        var document = (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Income);
        (document.Amount, document.CurrentAccountId).ShouldBe((50m, env.Supplier));
        await using var db       = PricingTestSupport.Db(env.Tenant);
        var             category = await db.ExpenseCategories.AsNoTracking().SingleAsync(c => c.Id == document.ExpenseCategoryId);
        category.Code.ShouldBe("kur-farki-geliri");
        (await List(env)).ShouldBeEmpty();
    }

    [Test]
    public async Task PayableNegativeDifference_ExpenseClosesSupplier() {
        // Alış 32,00'den yazıldı, 32,50'den ödendi: tedarikçide 50 TL borç artığı (fazla ödeme) kalır.
        var env  = await PayableScenario(32m, 32.5m);
        var item = (await List(env)).Single();
        (item.GetProperty("side").GetString(), item.GetProperty("difference").GetDecimal()).ShouldBe(("payable", -50m));

        using var response = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        await Created(response);
        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Expense).CurrentAccountId.ShouldBe(env.Supplier);
    }

    [Test]
    public async Task MixedKinds_AreRejected() {
        var env = await Scenario(32m, 32.5m);
        var usd = await Account(env, "USD Banka 2", "bank", "USD");
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 0)], extra: new { currency = "USD", rate = 32m }, date: new DateOnly(2026, 3, 2)));
        using (var response = await Collect(env, usd, 100m, method: "transfer", extra: new { rate = 31.5m })) await Created(response);

        var items = await List(env);
        items.Count.ShouldBe(2);
        var ids = items.SelectMany(Ids).ToArray();
        using var invoice = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = ids });
        await Fail(invoice, HttpStatusCode.BadRequest, "difference_not_applicable");
        using var expense = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = ids });
        await Fail(expense, HttpStatusCode.BadRequest, "difference_not_applicable");
        (await List(env)).Count.ShouldBe(2);
    }

    [Test]
    public async Task CancelExpense_ReleasesSettlement_AndDifferenceCanBeSettledAgain() {
        var env  = await Scenario(32m, 31.5m);
        var item = (await List(env)).Single();
        using var first = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(item) });
        var       id    = (await Created(first)).GetProperty("expenseId").GetGuid();
        (await List(env)).ShouldBeEmpty();

        using (var cancel = await CancelDocument(env, id)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        // Gider fişi iptal edildi: cari borcu geri geldi ve fark yeniden işlenebilir (bir kez).
        (await CariBalance(env, env.Customer)).ShouldBe(50m);
        var again = (await List(env)).Single();
        again.GetProperty("difference").GetDecimal().ShouldBe(-50m);
        using var second = await env.Client.PostAsJsonAsync($"{Url}/expense", new { allocationIds = Ids(again) });
        await Created(second);
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        (await List(env)).ShouldBeEmpty();
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.ExchangeDifferenceSettlements.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task DeleteDraftInvoice_ReleasesSettlement() {
        var env  = await Scenario(32m, 32.5m);
        var item = (await List(env)).Single();
        using var created = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        var       invoice = (await Created(created)).GetProperty("invoiceId").GetGuid();
        (await List(env)).ShouldBeEmpty();

        using (var delete = await env.Client.DeleteAsync($"/api/erp/invoices/{invoice}")) delete.StatusCode.ShouldBe(HttpStatusCode.NoContent, await delete.Content.ReadAsStringAsync());

        var again = (await List(env)).Single();
        again.GetProperty("difference").GetDecimal().ShouldBe(50m);
        using var second = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(again) });
        await Created(second);
    }

    [Test]
    public async Task CancelIssuedDifferenceInvoice_ReleasesSettlement() {
        var env  = await Scenario(32m, 32.5m);
        var item = (await List(env)).Single();
        using var created = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) });
        var       invoice = (await Created(created)).GetProperty("invoiceId").GetGuid();
        await Issue(env.Client, invoice);
        (await List(env)).ShouldBeEmpty();

        using (var cancel = await CancelInvoice(env, invoice)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        (await List(env)).Single().GetProperty("difference").GetDecimal().ShouldBe(50m);
    }

    [Test]
    public async Task CancelAndReenterPayment_SettledDifferenceIsNotListedAgain() {
        var env  = await Scenario(32m, 32.5m);
        var item = (await List(env)).Single();
        using (var created = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) })) await Created(created);

        // Hatalı tahsilat iptal edilir ve aynısı yeniden girilir: tahsis kimlikleri değişir, fark yine işlenmiş sayılır.
        var payment = (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Collection);
        using (var cancel = await CancelDocument(env, payment.Id)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        var usd = payment.AccountId!.Value;
        using (var again = await Collect(env, usd, 100m, method: "transfer", extra: new { rate = 32.5m })) await Created(again);

        (await List(env)).ShouldBeEmpty();
        var fresh = (await Allocations(env.Tenant)).Select(a => a.Id).Except(Ids(item)).ToArray();
        fresh.ShouldNotBeEmpty();
        using var second = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = fresh });
        await Fail(second, HttpStatusCode.Conflict, "difference_already_settled");
        await using var db = PricingTestSupport.Db(env.Tenant);
        (await db.Invoices.AsNoTracking().CountAsync(i => i.Kind == InvoiceKind.ExchangeDifference)).ShouldBe(1);
    }

    [Test]
    public async Task FifoShift_OnlyTheUnsettledDeltaIsListed() {
        var env  = await Scenario(32m, 32.5m);
        var item = (await List(env)).Single();
        using (var created = await env.Client.PostAsJsonAsync($"{Url}/invoice", new { allocationIds = Ids(item) })) await Created(created);
        (await List(env)).ShouldBeEmpty();

        // Daha eski tarihli TL fatura gelir: ilk tahsilatı FIFO ile o alır, USD fatura sonraki 33'lük tahsilata kayar.
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 3250m, vatRate: 0)], date: new DateOnly(2026, 2, 1)));
        var usd = (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Collection).AccountId!.Value;
        using (var second = await Collect(env, usd, 100m, method: "transfer", extra: new { rate = 33m })) await Created(second);

        // Yeni eşleşmenin farkı 100, ama 50'si zaten işlendi: yalnızca 50 listelenir.
        var rest = (await List(env)).Single();
        rest.GetProperty("difference").GetDecimal().ShouldBe(50m);
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
