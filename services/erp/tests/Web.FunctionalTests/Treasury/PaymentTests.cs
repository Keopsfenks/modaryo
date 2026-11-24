using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class PaymentTests : TestBase {
    [Test]
    public async Task Collection_CreditsCariAndIncreasesCash() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        await Invoice(env, 120m);
        (await CariBalance(env, env.Customer)).ShouldBe(120m);

        using var response = await Collect(env, kasa, 50m, extra: new { description = "Peşin tahsilat" });

        var body = await Created(response);
        body.GetProperty("number").GetString().ShouldBe("TH2026000001");
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        (await CariBalance(env, env.Customer)).ShouldBe(70m);
        (await AccountBalance(env, kasa)).ShouldBe(50m);
        var entry = (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Payment);
        entry.Credit.ShouldBe(50m);
        entry.SourceId.ShouldBe(body.GetProperty("id").GetGuid());
        entry.DocumentNo.ShouldBe("TH2026000001");
        var document = (await Documents(env.Tenant)).Single();
        (document.Kind, document.Amount, document.Currency, document.Method).ShouldBe((FinanceDocumentKind.Collection, 50m, "TRY", PaymentMethod.Cash));
        // İkinci belge numarayı artırır.
        using var second = await Collect(env, kasa, 10m);
        (await Created(second)).GetProperty("number").GetString().ShouldBe("TH2026000002");
        // Ödeme kendi önekini kullanır.
        using var payment = await Pay(env, kasa, 5m, extra: new { description = "İade" });
        (await Created(payment)).GetProperty("number").GetString().ShouldBe("OD2026000001");
    }

    [Test]
    public async Task Payment_DebitsCariAndDecreasesAccount() {
        var env  = await NewEnv();
        var bank = await Account(env, "Garanti TL", "bank");
        await Invoice(env, 240m, kind: "Purchase");
        (await CariBalance(env, env.Supplier)).ShouldBe(-240m);

        using var response = await Pay(env, bank, 100m, method: "transfer");

        (await Created(response)).GetProperty("number").GetString().ShouldBe("OD2026000001");
        (await CariBalance(env, env.Supplier)).ShouldBe(-140m);
        (await AccountBalance(env, bank)).ShouldBe(-100m); // banka ekside olabilir, uyarı yok
        (await Movements(env.Tenant)).Single().Amount.ShouldBe(-100m);
    }

    [Test]
    public async Task CardCollection_RequiresCardPosAccount() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var bank = await Account(env, "Garanti TL", "bank");
        var pos  = await Account(env, "Garanti POS", "cardPos");

        foreach (var wrong in new[] { kasa, bank }) {
            using var bad = await Collect(env, wrong, 100m, method: "creditCard");
            await Fail(bad, HttpStatusCode.BadRequest, "account_method_mismatch");
        }

        using var cashOnPos = await Collect(env, pos, 100m, method: "cash");
        await Fail(cashOnPos, HttpStatusCode.BadRequest, "account_method_mismatch");

        using var ok = await Collect(env, pos, 100m, method: "creditCard");
        await Created(ok);
        (await AccountBalance(env, pos)).ShouldBe(100m);
    }

    [Test]
    public async Task UsdPaymentToTryCari() {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await Invoice(env, 6000m, kind: "Purchase");

        using var response = await Pay(env, usd, 100m, method: "transfer", extra: new { rate = 32.50m });

        await Created(response);
        // Cariye TL (100 x 32,50), bankaya USD yazılır.
        (await AccountBalance(env, usd)).ShouldBe(-100m);
        var entry = (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Payment);
        (entry.Debit, entry.Currency, entry.SourceCurrency, entry.SourceAmount, entry.Rate).ShouldBe((3250m, "TRY", "USD", 100m, 32.50m));
        (await CariBalance(env, env.Supplier)).ShouldBe(-2750m);
        (await Movements(env.Tenant)).Single().Currency.ShouldBe("USD");
        (await Documents(env.Tenant)).Single().Rate.ShouldBe(32.50m);
    }

    [Test]
    public async Task TryPaymentToUsdCari_DividesByRate() {
        var env      = await NewEnv();
        var kasa     = await Account(env, "Merkez Kasa");
        var usdCari  = await CreateCari(env.Client, "USD1", customer: true, supplier: false, currency: "USD", name: "Dolar Müşteri");

        using var response = await Collect(env, kasa, 3250m, usdCari, extra: new { rate = 32.50m });

        await Created(response);
        var entry = (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Payment);
        (entry.Credit, entry.Currency, entry.SourceCurrency).ShouldBe((100m, "USD", "TRY"));
    }

    [Test]
    public async Task RateRequiredWhenCurrenciesDiffer_DefaultsToTcmb() {
        var env = await NewEnv();
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await ERP.Web.FunctionalTests.Invoicing.PostingSupport.Override(env, "USD", "2026-03-12", 33m);

        // Kur verilmezse o günün kuru (ayarlardaki kur türüyle) kullanılır.
        using var defaulted = await Pay(env, usd, 10m, method: "transfer");
        await Created(defaulted);
        (await Documents(env.Tenant)).Single().Rate.ShouldBe(33m);
        (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Payment).Debit.ShouldBe(330m);

        // Elle kur varsayılanı ezer.
        using var manual = await Pay(env, usd, 10m, method: "transfer", extra: new { rate = 34m });
        await Created(manual);
        (await Entries(env.Tenant)).Where(e => e.Source == LedgerEntrySource.Payment).Select(e => e.Debit).OrderBy(d => d).ShouldBe([330m, 340m]);

        // Kur bulunamayan tarih: kur zorunlu.
        using var missing = await Pay(env, usd, 10m, method: "transfer", date: new DateOnly(2012, 1, 5));
        await Fail(missing, HttpStatusCode.BadRequest, "rate_required");
    }

    [Test]
    public async Task PartialPayment_ClosesOneAndAHalfInstallments_CancelRestores() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        object[] schedule = [new { dueDate = "2026-04-10", amount = 100m }, new { dueDate = "2026-05-10", amount = 100m }, new { dueDate = "2026-06-10", amount = 100m }];
        await Invoice(env, 300m, schedule: schedule);
        (await DueItems(env, env.Customer)).Select(i => i.GetProperty("remaining").GetDecimal()).ShouldBe([100m, 100m, 100m]);

        var payment = await CollectOk(env, kasa, 150m);

        var open = await DueItems(env, env.Customer);
        open.Select(i => (i.GetProperty("sequence").GetInt32(), i.GetProperty("remaining").GetDecimal())).ShouldBe([(2, 50m), (3, 100m)]);

        using var cancel = await CancelDocument(env, payment);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        (await DueItems(env, env.Customer)).Select(i => (i.GetProperty("sequence").GetInt32(), i.GetProperty("remaining").GetDecimal()))
            .ShouldBe([(1, 100m), (2, 100m), (3, 100m)]);
        (await CariBalance(env, env.Customer)).ShouldBe(300m);
        (await AccountBalance(env, kasa)).ShouldBe(0m);
        var document = (await Documents(env.Tenant)).Single();
        document.IsCancelled.ShouldBeTrue();
        document.CancelReason.ShouldBe("Hatalı giriş");
        (await Movements(env.Tenant)).Single().IsCancelled.ShouldBeTrue();

        using var again = await CancelDocument(env, payment);
        await Fail(again, HttpStatusCode.Conflict, "document_cancelled");
    }

    [Test]
    public async Task NegativeCash_ReturnsWarning() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        await CollectOk(env, kasa, 100m);

        using var fine = await Pay(env, kasa, 60m);
        (await Created(fine)).GetProperty("warnings").GetArrayLength().ShouldBe(0);

        using var over = await Pay(env, kasa, 60m);

        var body = await Created(over);
        body.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldBe(["cash_negative"]);
        (await AccountBalance(env, kasa)).ShouldBe(-20m);
    }

    [Test]
    public async Task PeriodLocked_Returns409() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var id   = await CollectOk(env, kasa, 100m, date: new DateOnly(2026, 2, 10));
        await LockPeriod(env.Client, "2026-02-28");

        using var locked = await Collect(env, kasa, 10m, date: new DateOnly(2026, 2, 20));
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict, await locked.Content.ReadAsStringAsync());
        using var cancel = await CancelDocument(env, id);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict, await cancel.Content.ReadAsStringAsync());

        using var open = await Collect(env, kasa, 10m, date: new DateOnly(2026, 3, 2));
        await Created(open);
        (await AccountBalance(env, kasa)).ShouldBe(110m);
    }

    [Test]
    public async Task AdvanceThenInvoice_AutoAllocates() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        var orderId = Guid.NewGuid();
        var advance = await CollectOk(env, kasa, 80m, extra: new { isAdvance = true, salesOrderId = orderId }, date: new DateOnly(2026, 2, 20));
        (await Documents(env.Tenant)).Single().IsAdvance.ShouldBeTrue();
        (await Documents(env.Tenant)).Single().SalesOrderId.ShouldBe(orderId);
        (await DueItems(env, env.Customer)).Single().GetProperty("remaining").GetDecimal().ShouldBe(80m); // açık alacak (avans)

        await Invoice(env, 100m);

        var open = await DueItems(env, env.Customer);
        open.Single().GetProperty("remaining").GetDecimal().ShouldBe(20m);
        (await Allocations(env.Tenant, AllocationKind.Auto)).Sum(a => a.Amount).ShouldBe(80m);
        advance.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task Validation_AndPermissions() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");

        using var zero = await Collect(env, kasa, 0m);
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var scale = await Collect(env, kasa, 1.234m);
        scale.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var noCari = await Collect(env, kasa, 10m, Guid.NewGuid());
        noCari.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var noAccount = await Collect(env, Guid.NewGuid(), 10m);
        noAccount.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var readOnly = await TestEnvironment.Factory.ClientAsync(env.Tenant, "finans.read");
        using var forbidden = await readOnly.PostAsJsonAsync($"{Base}/collections", new { currentAccountId = env.Customer, accountId = kasa, method = "cash", date = "2026-03-12", amount = 5m });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var forbiddenPayment = await readOnly.PostAsJsonAsync($"{Base}/payments", new { currentAccountId = env.Supplier, accountId = kasa, method = "cash", date = "2026-03-12", amount = 5m });
        forbiddenPayment.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Pasif hesap.
        using var deactivate = await env.Client.PostAsync($"{Base}/accounts/{kasa}/deactivate", null);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var inactive = await Collect(env, kasa, 10m);
        await Fail(inactive, HttpStatusCode.Conflict, "account_inactive");
    }

    [Test]
    public async Task PaymentEntry_IsNotCancellableFromTheCariCard() {
        var env  = await NewEnv();
        var kasa = await Account(env, "Merkez Kasa");
        await CollectOk(env, kasa, 10m);
        var entry = (await Entries(env.Tenant)).Single();

        using var response = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/entries/{entry.Id}/cancel", new { reason = "deneme" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
    }
}
