using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class ExpenseTests : TestBase {
    private const string Categories = $"{Base}/expense-categories";

    private static async Task<JsonElement> Category(Env env, string code) {
        using var response = await env.Client.GetAsync(Categories);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().First(c => c.GetProperty("code").GetString() == code);
    }

    private static Task<HttpResponseMessage> Expense(Env env, Guid category, decimal amount, string kind = "expense", Guid? account = null, Guid? cari = null, decimal? vatRate = null,
                                                     DateOnly? date = null) {
        return env.Client.PostAsJsonAsync($"{Base}/expenses", new {
            kind, categoryId = category, date = (date ?? Today).ToString("yyyy-MM-dd"), amount, vatRate, accountId = account, currentAccountId = cari, description = "Masraf"
        });
    }

    [Test]
    public async Task Expense_FromCash_SplitsVat() {
        var env      = await NewEnv();
        var cash     = await Account(env, "Kasa", "cash");
        var category = (await Category(env, "genel")).GetProperty("id").GetGuid();

        using var response = await Expense(env, category, 120m, account: cash, vatRate: 20m);
        var       body     = await Created(response);

        body.GetProperty("number").GetString().ShouldBe("MF2026000001");
        body.GetProperty("netAmount").GetDecimal().ShouldBe(100m);
        body.GetProperty("vatAmount").GetDecimal().ShouldBe(20m);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain("cash_negative");
        (await AccountBalance(env, cash)).ShouldBe(-120m);
        var document = (await Documents(env.Tenant)).Single();
        (document.Kind, document.Amount, document.VatRate, document.ExpenseCategoryId).ShouldBe((FinanceDocumentKind.Expense, 120m, 20m, category));
    }

    [Test]
    public async Task Income_ToBank_PrefixesGfAndAddsBalance() {
        var env      = await NewEnv();
        var bank     = await Account(env, "Banka", "bank");
        var category = (await Category(env, "genel-gelir")).GetProperty("id").GetGuid();

        using var response = await Expense(env, category, 60m, "income", account: bank, vatRate: 20m);
        var       body     = await Created(response);

        body.GetProperty("number").GetString().ShouldBe("GF2026000001");
        (await AccountBalance(env, bank)).ShouldBe(60m);
        (await Documents(env.Tenant)).Single().Kind.ShouldBe(FinanceDocumentKind.Income);
    }

    [Test]
    public async Task Expense_OnCari_CreditsCari() {
        var env      = await NewEnv();
        var category = (await Category(env, "kira")).GetProperty("id").GetGuid();

        using var response = await Expense(env, category, 240m, cari: env.Supplier, vatRate: 20m);
        await Created(response);

        (await CariBalance(env, env.Supplier)).ShouldBe(-240m);
        var entry = (await Entries(env.Tenant)).Single();
        entry.Credit.ShouldBe(240m);
        entry.Source.ShouldBe(LedgerEntrySource.Payment);
        (await Movements(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Expense_NeitherOrBothTargets_Returns400() {
        var env      = await NewEnv();
        var cash     = await Account(env, "Kasa", "cash");
        var category = (await Category(env, "genel")).GetProperty("id").GetGuid();

        using (var none = await Expense(env, category, 10m))
            none.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var both = await Expense(env, category, 10m, account: cash, cari: env.Supplier))
            both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Expense_WithIncomeCategory_Returns400() {
        var env      = await NewEnv();
        var cash     = await Account(env, "Kasa", "cash");
        var category = (await Category(env, "genel-gelir")).GetProperty("id").GetGuid();

        using var response = await Expense(env, category, 10m, account: cash);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("category_kind_mismatch");
    }

    [Test]
    public async Task CancelExpense_Reverses() {
        var env      = await NewEnv();
        var cash     = await Account(env, "Kasa", "cash");
        var category = (await Category(env, "genel")).GetProperty("id").GetGuid();
        using var cashExpense = await Expense(env, category, 120m, account: cash, vatRate: 20m);
        var       cashId      = (await Created(cashExpense)).GetProperty("id").GetGuid();
        using var cariExpense = await Expense(env, category, 240m, cari: env.Supplier);
        var       cariId      = (await Created(cariExpense)).GetProperty("id").GetGuid();

        using (var cancel = await CancelDocument(env, cashId))
            cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        using (var cancel = await CancelDocument(env, cariId))
            cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        (await AccountBalance(env, cash)).ShouldBe(0m);
        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        using var again = await CancelDocument(env, cashId);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task ExpenseCategories_SeededAndCrud() {
        var env = await NewEnv();
        (await Category(env, "kur-farki")).GetProperty("accountCode").GetString().ShouldBe("656");

        using var created = await env.Client.PostAsJsonAsync(Categories, new { name = "Şirket Aracı Yakıt", accountCode = "770.05", isIncome = false });
        var       body    = await Created(created);
        body.GetProperty("code").GetString().ShouldBe("sirket-araci-yakit");
        var id = body.GetProperty("id").GetGuid();

        using var duplicate = await env.Client.PostAsJsonAsync(Categories, new { name = "Şirket aracı yakıt", accountCode = "770" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var update = await env.Client.PutAsJsonAsync($"{Categories}/{id}", new { name = "Yakıt", accountCode = "770.06" });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        (await update.JsonAsync()).GetProperty("accountCode").GetString().ShouldBe("770.06");

        using var system = await env.Client.DeleteAsync($"{Categories}/{(await Category(env, "genel")).GetProperty("id").GetGuid()}");
        system.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var cash = await Account(env, "Kasa", "cash");
        using (var used = await Expense(env, id, 10m, account: cash))
            await Created(used);
        using var inUse = await env.Client.DeleteAsync($"{Categories}/{id}");
        inUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var other   = await env.Client.PostAsJsonAsync(Categories, new { name = "Kullanılmayan", accountCode = "770" });
        var       otherId = (await Created(other)).GetProperty("id").GetGuid();
        using var deleted = await env.Client.DeleteAsync($"{Categories}/{otherId}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Expense_WithoutPermission_Returns403() {
        var env = await NewEnv();
        using var client   = await OtherClient(env, "finans.read");
        using var response = await client.PostAsJsonAsync($"{Base}/expenses", new { kind = "expense", categoryId = Guid.NewGuid(), date = "2026-03-12", amount = 10m });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
