using ERP.Domain.Treasury;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;


public class TransferTests : TestBase {
    private static Task<HttpResponseMessage> Transfer(Env env, Guid from, Guid to, decimal amount, decimal? rate = null, DateOnly? date = null) {
        return env.Client.PostAsJsonAsync($"{Base}/transfers", new {
            fromAccountId = from, toAccountId = to, date = (date ?? Today).ToString("yyyy-MM-dd"), amount, rate, description = "Virman"
        });
    }

    [Test]
    public async Task Transfer_SameCurrency_MovesBothAccounts() {
        var env  = await NewEnv();
        var cash = await Account(env, "Kasa", "cash");
        var bank = await Account(env, "Banka", "bank");
        await CollectOk(env, cash, 1000m);

        using var response = await Transfer(env, cash, bank, 400m);
        var       body     = await Created(response);

        body.GetProperty("number").GetString()!.ShouldStartWith("VR2026");
        (await AccountBalance(env, cash)).ShouldBe(600m);
        (await AccountBalance(env, bank)).ShouldBe(400m);
        var document = (await Documents(env.Tenant)).Single(d => d.Kind == FinanceDocumentKind.Transfer);
        document.AccountId.ShouldBe(cash);
        document.TargetAccountId.ShouldBe(bank);
    }

    [Test]
    public async Task Transfer_UsdToTry_UsesRate() {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        var tl  = await Account(env, "TL Banka", "bank");

        using var response = await Transfer(env, usd, tl, 100m, 32.5m);
        await Created(response);

        (await AccountBalance(env, usd)).ShouldBe(-100m);
        (await AccountBalance(env, tl)).ShouldBe(3250m);

        // TL -> USD ters yöne bölünür.
        using var back = await Transfer(env, tl, usd, 3250m, 32.5m);
        await Created(back);
        (await AccountBalance(env, usd)).ShouldBe(0m);
        (await AccountBalance(env, tl)).ShouldBe(0m);
    }

    [Test]
    public async Task Transfer_DifferentCurrencyWithoutRate_Returns400() {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        var tl  = await Account(env, "TL Banka", "bank");

        using var response = await Transfer(env, usd, tl, 100m);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("rate_required");
    }

    [Test]
    public async Task Transfer_SameAccount_Returns400() {
        var env  = await NewEnv();
        var cash = await Account(env, "Kasa", "cash");

        using var response = await Transfer(env, cash, cash, 10m);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Transfer_FromCashBelowZero_WarnsAndCancelReverses() {
        var env  = await NewEnv();
        var cash = await Account(env, "Kasa", "cash");
        var bank = await Account(env, "Banka", "bank");

        using var response = await Transfer(env, cash, bank, 50m);
        var       body     = await Created(response);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldContain("cash_negative");
        (await AccountBalance(env, cash)).ShouldBe(-50m);

        using var cancel = await CancelDocument(env, body.GetProperty("id").GetGuid());
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        (await AccountBalance(env, cash)).ShouldBe(0m);
        (await AccountBalance(env, bank)).ShouldBe(0m);
    }

    [Test]
    public async Task Transfer_InLockedPeriod_Returns409() {
        var env  = await NewEnv();
        var cash = await Account(env, "Kasa", "cash");
        var bank = await Account(env, "Banka", "bank");
        await LockPeriod(env.Client, "2026-03-31");

        using var response = await Transfer(env, cash, bank, 10m);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
