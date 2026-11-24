using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class InstrumentTests : TestBase {
    private static readonly DateOnly Due = new(2026, 4, 30);

    private static Task<Env> InstrumentEnv(string[]? modules = null) {
        return InvoiceTestSupport_NewEnv(modules ?? ["cek-senet"]);
    }

    private static Task<Env> InvoiceTestSupport_NewEnv(string[] modules) {
        return ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport.NewEnv(AllPermissions.Concat(TreasuryTestSupport.Permissions).Distinct().ToArray(), null, "P1", modules);
    }

    private static Task<HttpResponseMessage> Receive(Env env, string number = "C-1", decimal amount = 100m, string kind = "check", Guid? cari = null, string? bank = "Garanti", DateOnly? date = null) {
        return env.Client.PostAsJsonAsync($"{Base}/instruments/receive", new {
            kind, currentAccountId = cari ?? env.Customer, number, dueDate = Due.ToString("yyyy-MM-dd"), amount, currency = "TRY", drawer = "Keşideci A.Ş.", bankName = bank,
            bankBranch = "Kadıköy", accountNo = "12345", date = (date ?? Today).ToString("yyyy-MM-dd")
        });
    }

    private static Task<HttpResponseMessage> Issue(Env env, Guid? bankAccount, string number = "OC-1", decimal amount = 100m, string kind = "check") {
        return env.Client.PostAsJsonAsync($"{Base}/instruments/issue", new {
            kind, currentAccountId = env.Supplier, number, dueDate = Due.ToString("yyyy-MM-dd"), amount, currency = "TRY", bankAccountId = bankAccount, date = Today.ToString("yyyy-MM-dd")
        });
    }

    private static Task<HttpResponseMessage> Move(Env env, Guid id, string to, Guid? cari = null, Guid? account = null, DateOnly? date = null) {
        return env.Client.PostAsJsonAsync($"{Base}/instruments/{id}/move", new { to, date = (date ?? Today).ToString("yyyy-MM-dd"), currentAccountId = cari, accountId = account, note = "Not" });
    }

    private static async Task<Guid> ReceiveOk(Env env, string number = "C-1", decimal amount = 100m, string kind = "check") {
        using var response = await Receive(env, number, amount, kind, bank: kind == "check" ? "Garanti" : null);
        return (await Created(response)).GetProperty("instrumentId").GetGuid();
    }

    private static async Task MoveOk(Env env, Guid id, string to, Guid? cari = null, Guid? account = null, DateOnly? date = null) {
        using var response = await Move(env, id, to, cari, account, date);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> Get(Env env, Guid id) {
        using var response = await env.Client.GetAsync($"{Base}/instruments/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<string> Status(Env env, Guid id) {
        return (await Get(env, id)).GetProperty("instrument").GetProperty("status").GetString()!;
    }

    private static async Task ShouldFail(Task<HttpResponseMessage> call, HttpStatusCode status, string code) {
        using var response = await call;
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Test]
    public async Task ReceiveCheck_CreditsCustomer() {
        var env = await InstrumentEnv();
        await Invoice(env, 120m);

        using var response = await Receive(env, amount: 100m);
        var       body     = await Created(response);

        body.GetProperty("documentNumber").GetString().ShouldBe("CS2026000001");
        (await CariBalance(env, env.Customer)).ShouldBe(20m);
        var entry = (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Check);
        (entry.Credit, entry.DocumentNo).ShouldBe((100m, "CS2026000001"));
        var detail = await Get(env, body.GetProperty("instrumentId").GetGuid());
        detail.GetProperty("instrument").GetProperty("status").GetString().ShouldBe("inPortfolio");
        detail.GetProperty("history").GetArrayLength().ShouldBe(1);

        await ShouldFail(Receive(env, amount: 100m), HttpStatusCode.Conflict, "instrument_number_taken");
        // Aynı numara başka bankada ya da senet olarak ayrı kayıttır.
        using var otherBank = await Receive(env, bank: "Akbank");
        await Created(otherBank);
        using var note = await Receive(env, kind: "note", bank: null);
        await Created(note);
    }

    [Test]
    public async Task ReceiveCheck_WithoutBankOrForOtherRole_Returns400() {
        var env = await InstrumentEnv();

        using (var noBank = await Receive(env, bank: null))
            noBank.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await ShouldFail(Receive(env, cari: env.Supplier), HttpStatusCode.BadRequest, "cari_role");
    }

    [Test]
    public async Task Endorse_DebitsSupplier() {
        var env = await InstrumentEnv();
        var id  = await ReceiveOk(env);

        await MoveOk(env, id, "endorsed", env.Supplier);

        (await CariBalance(env, env.Supplier)).ShouldBe(100m);
        (await CariBalance(env, env.Customer)).ShouldBe(-100m);
        var instrument = (await Get(env, id)).GetProperty("instrument");
        instrument.GetProperty("status").GetString().ShouldBe("endorsed");
        instrument.GetProperty("endorsementCount").GetInt32().ShouldBe(1);
        instrument.GetProperty("endorsedToId").GetGuid().ShouldBe(env.Supplier);
        await ShouldFail(Move(env, id, "endorsed", env.Supplier), HttpStatusCode.Conflict, "invalid_instrument_transition");
    }

    [Test]
    public async Task DepositAndCollect_MovesBank() {
        var env  = await InstrumentEnv();
        var bank = await Account(env, "Banka", "bank");
        var id   = await ReceiveOk(env);

        await MoveOk(env, id, "inCollection", account: bank);
        (await AccountBalance(env, bank)).ShouldBe(0m);
        (await CariBalance(env, env.Customer)).ShouldBe(-100m);

        await MoveOk(env, id, "collected", date: Due);
        (await AccountBalance(env, bank)).ShouldBe(100m);
        (await CariBalance(env, env.Customer)).ShouldBe(-100m);
        (await Get(env, id)).GetProperty("history").GetArrayLength().ShouldBe(3);
    }

    [Test]
    public async Task CollectionMustUseBankAccountOfSameCurrency() {
        var env  = await InstrumentEnv();
        var cash = await Account(env, "Kasa", "cash");
        var usd  = await Account(env, "USD Banka", "bank", "USD");
        var id   = await ReceiveOk(env);

        await ShouldFail(Move(env, id, "inCollection", account: cash), HttpStatusCode.BadRequest, "account_method_mismatch");
        await ShouldFail(Move(env, id, "inCollection", account: usd), HttpStatusCode.BadRequest, "account_currency_mismatch");
        await ShouldFail(Move(env, id, "inCollection"), HttpStatusCode.BadRequest, "account_required");
    }

    [Test]
    public async Task EndorsedCheckBounces_RestoresBothCaris() {
        var env = await InstrumentEnv();
        await Invoice(env, 100m);
        var id = await ReceiveOk(env);
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        await MoveOk(env, id, "endorsed", env.Supplier);
        (await CariBalance(env, env.Supplier)).ShouldBe(100m);

        await MoveOk(env, id, "bounced");

        // Tedarikçi borcumuzu geri alır (alacak), müşteri yeniden borçludur.
        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        (await CariBalance(env, env.Customer)).ShouldBe(100m);
        var detail = await Get(env, id);
        detail.GetProperty("instrument").GetProperty("status").GetString().ShouldBe("bounced");
        detail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("to").GetString()).ShouldBe(["inPortfolio", "endorsed", "bounced"]);

        // Geç tahsilat nakit kasaya girer ve müşterinin borcunu kapatır.
        var cash = await Account(env, "Kasa", "cash");
        await MoveOk(env, id, "collected", account: cash);
        (await AccountBalance(env, cash)).ShouldBe(100m);
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
    }

    [Test]
    public async Task BouncedFromCollection_DebitsCustomerAndFlagsProtest() {
        var env  = await InstrumentEnv();
        var bank = await Account(env, "Banka", "bank");
        var id   = await ReceiveOk(env);
        await MoveOk(env, id, "inCollection", account: bank);

        await MoveOk(env, id, "bounced");

        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        (await AccountBalance(env, bank)).ShouldBe(0m);
        (await Get(env, id)).GetProperty("instrument").GetProperty("protestFlag").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task ReturnToCustomer_DebitsCustomer() {
        var env = await InstrumentEnv();
        var id  = await ReceiveOk(env);

        await MoveOk(env, id, "returnedToCustomer");

        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        await ShouldFail(Move(env, id, "collected"), HttpStatusCode.Conflict, "invalid_instrument_transition");
    }

    [Test]
    public async Task IssueOwnCheck_DebitsSupplier_PaidMovesBank() {
        var env  = await InstrumentEnv();
        var bank = await Account(env, "Banka", "bank");
        await Invoice(env, 100m, kind: "Purchase");
        (await CariBalance(env, env.Supplier)).ShouldBe(-100m);

        using var response = await Issue(env, bank);
        var       id       = (await Created(response)).GetProperty("instrumentId").GetGuid();

        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        (await Status(env, id)).ShouldBe("issued");
        (await Get(env, id)).GetProperty("instrument").GetProperty("bankName").GetString().ShouldBe("Garanti");

        await ShouldFail(Move(env, id, "paid", date: Due.AddDays(-1)), HttpStatusCode.Conflict, "instrument_not_due");
        await MoveOk(env, id, "paid", date: Due);

        (await AccountBalance(env, bank)).ShouldBe(-100m);
        (await Status(env, id)).ShouldBe("paid");
        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
    }

    [Test]
    public async Task IssuedReturnedFromSupplier_CreditsSupplier() {
        var env  = await InstrumentEnv();
        var bank = await Account(env, "Banka", "bank");
        using var response = await Issue(env, bank, kind: "note");
        var       id       = (await Created(response)).GetProperty("instrumentId").GetGuid();
        (await CariBalance(env, env.Supplier)).ShouldBe(100m);

        await MoveOk(env, id, "returnedFromSupplier");

        (await CariBalance(env, env.Supplier)).ShouldBe(0m);
        await ShouldFail(Issue(env, null), HttpStatusCode.BadRequest, "bank_account_required");
    }

    [Test]
    public async Task InvalidTransition_Returns409() {
        var env = await InstrumentEnv();
        var id  = await ReceiveOk(env);

        await ShouldFail(Move(env, id, "collected"), HttpStatusCode.Conflict, "invalid_instrument_transition");
        await ShouldFail(Move(env, id, "paid"), HttpStatusCode.Conflict, "invalid_instrument_transition");
        (await Status(env, id)).ShouldBe("inPortfolio");
    }

    [Test]
    public async Task ModuleDisabled_Returns403() {
        var env = await InstrumentEnv(["pos"]);

        await ShouldFail(Receive(env), HttpStatusCode.Forbidden, "module_disabled");
        using var list = await env.Client.GetAsync($"{Base}/instruments");
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var portfolio = await env.Client.GetAsync($"{Base}/instruments/portfolio");
        portfolio.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PortfolioTotalsByStatus() {
        var env  = await InstrumentEnv();
        var bank = await Account(env, "Banka", "bank");
        var a    = await ReceiveOk(env, "C-1", 100m);
        var b    = await ReceiveOk(env, "C-2", 250m);
        await ReceiveOk(env, "C-3", 40m);
        await MoveOk(env, b, "inCollection", account: bank);
        await MoveOk(env, a, "endorsed", env.Supplier);

        using var response = await env.Client.GetAsync($"{Base}/instruments/portfolio");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rows = (await response.JsonAsync()).EnumerateArray().ToDictionary(r => r.GetProperty("status").GetString()!, r => (r.GetProperty("count").GetInt32(), r.GetProperty("total").GetDecimal()));

        rows["inPortfolio"].ShouldBe((1, 40m));
        rows["inCollection"].ShouldBe((1, 250m));
        rows["endorsed"].ShouldBe((1, 100m));

        using var filtered = await env.Client.GetAsync($"{Base}/instruments?Status=InCollection&Direction=Received");
        (await filtered.JsonAsync()).GetArrayLength().ShouldBe(1);
        using var calendar = await env.Client.GetAsync($"{Base}/instruments/calendar?from=2026-04-01&to=2026-04-30");
        var       days     = (await calendar.JsonAsync()).EnumerateArray().ToList();
        days.Count.ShouldBe(1);
        days[0].GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task MoveInLockedPeriod_Returns409() {
        var env = await InstrumentEnv();
        var id  = await ReceiveOk(env);
        await LockPeriod(env.Client, "2026-03-31");

        await ShouldFail(Move(env, id, "returnedToCustomer", date: new DateOnly(2026, 3, 20)), HttpStatusCode.Conflict, "period_locked");
        (await Status(env, id)).ShouldBe("inPortfolio");
        await ShouldFail(Receive(env, "C-9", date: new DateOnly(2026, 3, 20)), HttpStatusCode.Conflict, "period_locked");

        await MoveOk(env, id, "returnedToCustomer", date: new DateOnly(2026, 4, 2));
    }

    [Test]
    public async Task ParallelMoves_OnlyOneWins() {
        var env = await InstrumentEnv();
        var id  = await ReceiveOk(env);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => {
            using var response = await Move(env, id, "endorsed", env.Supplier);
            return response.StatusCode;
        }));

        results.Count(s => s == HttpStatusCode.OK).ShouldBe(1);
        results.Count(s => s == HttpStatusCode.Conflict).ShouldBe(5);
        (await CariBalance(env, env.Supplier)).ShouldBe(100m);
    }

    [Test]
    public async Task WithoutPermission_Returns403() {
        var env = await InstrumentEnv();
        using var client   = await OtherClient(env, "finans.read");
        using var response = await client.PostAsJsonAsync($"{Base}/instruments/receive", new { kind = "check" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
