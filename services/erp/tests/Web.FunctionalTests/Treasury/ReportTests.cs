using System.Text.Json;
using ERP.Web.FunctionalTests.Invoicing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class ReportTests : TestBase {
    private const string AsOf = "2026-06-30";

    private static Task<Env> ReportEnv() {
        return InvoiceTestSupport.NewEnv(AllPermissions.Concat(TreasuryTestSupport.Permissions).Distinct().ToArray(), null, "P1", ["cek-senet"]);
    }

    private static async Task<JsonElement> Get(Env env, string url) {
        using var response = await env.Client.GetAsync($"{Base}/reports/{url}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static decimal Bucket(JsonElement buckets, string name) {
        return buckets.GetProperty(name).GetDecimal();
    }

    private static decimal[] Buckets(JsonElement buckets) {
        return new[] { "current", "1-30", "31-60", "61-90", "90+" }.Select(b => Bucket(buckets, b)).ToArray();
    }

    /// <summary>Vadesi tarihin kendisi olan (vade 0 gün) KDV dahil fatura keser; cari verilmezse müşteri/tedarikçi.</summary>
    private static async Task DueOn(Env env, decimal gross, string date, string kind = "Sales", Guid? cari = null) {
        var net  = Math.Round(gross / 1.2m, 2);
        var body = Body(env, kind, [FreeLine("Hizmet", 1m, net, 20)], new { paymentTermDays = 0, currentAccountId = cari ?? (kind == "Purchase" ? env.Supplier : env.Customer) }, DateOnly.Parse(date));
        await CreateAndIssue(env, body);
    }

    [Test]
    public async Task Aging_BucketsPerCari() {
        var env   = await ReportEnv();
        var other = await CreateCari(env.Client, "M2", customer: true, supplier: false);

        await DueOn(env, 120m, "2026-06-30");  // vadesi bugün: current
        await DueOn(env, 240m, "2026-06-15");  // 15 gün: 1-30
        await DueOn(env, 360m, "2026-05-10");  // 51 gün: 31-60
        await DueOn(env, 480m, "2026-04-01");  // 90 gün: 61-90
        await DueOn(env, 600m, "2026-03-01");  // 121 gün: 90+
        await DueOn(env, 60m, "2026-07-10", cari: other); // vadesi gelmemiş: current

        // 100 TL'lik tahsilat en eski (90+) kalemi kapatmaya başlar.
        await CollectOk(env, await Account(env, "Kasa"), 100m);

        var report = await Get(env, $"aging?direction=receivable&asOf={AsOf}");

        report.GetProperty("asOf").GetString().ShouldBe(AsOf);
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(2);
        var first = rows.Single(r => r.GetProperty("code").GetString() == "M1");
        Buckets(first.GetProperty("buckets")).ShouldBe([120m, 240m, 360m, 480m, 500m]);
        first.GetProperty("total").GetDecimal().ShouldBe(1700m);
        first.GetProperty("currency").GetString().ShouldBe("TRY");
        var second = rows.Single(r => r.GetProperty("code").GetString() == "M2");
        Buckets(second.GetProperty("buckets")).ShouldBe([60m, 0m, 0m, 0m, 0m]);

        var total = report.GetProperty("totals").EnumerateArray().Single();
        total.GetProperty("currency").GetString().ShouldBe("TRY");
        Buckets(total.GetProperty("buckets")).ShouldBe([180m, 240m, 360m, 480m, 500m]);
        total.GetProperty("total").GetDecimal().ShouldBe(1760m);

        // Aynı veride borç yönünde kalem yok.
        (await Get(env, $"aging?direction=payable&asOf={AsOf}")).GetProperty("rows").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Aging_PayableDirection() {
        var env = await ReportEnv();
        await DueOn(env, 240m, "2026-06-20", "Purchase");
        await DueOn(env, 120m, "2026-03-01", "Purchase");

        var report = await Get(env, $"aging?direction=payable&asOf={AsOf}");

        var row = report.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("code").GetString().ShouldBe("T1");
        Buckets(row.GetProperty("buckets")).ShouldBe([0m, 240m, 0m, 0m, 120m]);
        report.GetProperty("totals").EnumerateArray().Single().GetProperty("total").GetDecimal().ShouldBe(360m);

        // Alacak yönünde bu cari görünmez; olmayan para birimi filtresi boş döner.
        (await Get(env, $"aging?direction=receivable&asOf={AsOf}")).GetProperty("rows").GetArrayLength().ShouldBe(0);
        (await Get(env, $"aging?direction=payable&currency=USD&asOf={AsOf}")).GetProperty("rows").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Aging_InvalidParameters_Return400() {
        var env = await ReportEnv();

        using var direction = await env.Client.GetAsync($"{Base}/reports/aging?direction=sideways");
        direction.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var currency = await env.Client.GetAsync($"{Base}/reports/aging?direction=receivable&currency=XXX");
        currency.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CashFlow_IncludesInstrumentsAndDueLines() {
        var env  = await ReportEnv();
        var cash = await Account(env, "Kasa");
        await TreasuryTestSupport.AddMovementAsync(env.Tenant, cash, 1000m, Today);

        // Satış 3/21'de vadeli 120, alış 3/31'de vadeli 240.
        await CreateAndIssue(env, Body(env, lines: [FreeLine("Hizmet", 1m, 100m, 20)], extra: new { paymentTermDays = 20 }, date: new DateOnly(2026, 3, 1)));
        await CreateAndIssue(env, Body(env, "Purchase", [FreeLine("Hizmet", 1m, 200m, 20)], new { paymentTermDays = 30 }, new DateOnly(2026, 3, 1)));

        // Alınan çek 3/20 vadeli 100 (müşteri alacağını 100 kapatır: 20 kalır); verilen senet 3/27 vadeli 50 (tedarikçi borcu 190 kalır).
        using (var check = await env.Client.PostAsJsonAsync($"{Base}/instruments/receive", new {
                   kind = "check", currentAccountId = env.Customer, number = "C-1", dueDate = "2026-03-20", amount = 100m, currency = "TRY", drawer = "Keşideci", bankName = "Garanti", date = "2026-03-12"
               }))
            await Created(check);
        using (var note = await env.Client.PostAsJsonAsync($"{Base}/instruments/issue", new {
                   kind = "note", currentAccountId = env.Supplier, number = "N-1", dueDate = "2026-03-27", amount = 50m, currency = "TRY", date = "2026-03-12"
               }))
            await Created(note);

        var report = await Get(env, "cash-flow?granularity=week&periods=4&from=2026-03-12");

        report.GetProperty("opening").GetDecimal().ShouldBe(1000m);
        var periods = report.GetProperty("periods").EnumerateArray().ToList();
        periods.Count.ShouldBe(4);
        (periods[0].GetProperty("start").GetString(), periods[0].GetProperty("end").GetString()).ShouldBe(("2026-03-12", "2026-03-18"));
        periods[0].GetProperty("net").GetDecimal().ShouldBe(0m);

        // Hafta 2 (3/19-3/25): çek 100 + kalan vade satırı 20.
        periods[1].GetProperty("inflow").GetProperty("instruments").GetDecimal().ShouldBe(100m);
        periods[1].GetProperty("inflow").GetProperty("dueLines").GetDecimal().ShouldBe(20m);
        periods[1].GetProperty("inflow").GetProperty("recurring").GetDecimal().ShouldBe(0m);
        periods[1].GetProperty("closing").GetDecimal().ShouldBe(1120m);

        // Hafta 3 (3/26-4/1): senet 50 + kalan alış vadesi 190.
        periods[2].GetProperty("outflow").GetProperty("instruments").GetDecimal().ShouldBe(50m);
        periods[2].GetProperty("outflow").GetProperty("dueLines").GetDecimal().ShouldBe(190m);
        periods[2].GetProperty("net").GetDecimal().ShouldBe(-240m);
        periods[2].GetProperty("closing").GetDecimal().ShouldBe(880m);
        periods[3].GetProperty("net").GetDecimal().ShouldBe(0m);
        report.GetProperty("closing").GetDecimal().ShouldBe(880m);
    }

    [Test]
    public async Task CashFlow_AdvancesAndPrepayments_AreNotFutureCash() {
        var env  = await ReportEnv();
        var cash = await Account(env, "Kasa");
        var date = new DateOnly(2026, 3, 12);

        // Müşteri avansı (tahsilat, faturasız) 500 kasaya girdi; tedarikçiye avans ödemesi 200 çıktı; faturasız alınan çek 100.
        using (var advance = await Collect(env, cash, 500m, extra: new { isAdvance = true }, date: date)) await Created(advance);
        using (var prepayment = await Pay(env, cash, 200m, extra: new { isAdvance = true }, date: date)) await Created(prepayment);
        using (var check = await env.Client.PostAsJsonAsync($"{Base}/instruments/receive", new {
                   kind = "check", currentAccountId = env.Customer, number = "C-9", dueDate = "2026-03-20", amount = 100m, currency = "TRY", drawer = "Keşideci", bankName = "Garanti", date = "2026-03-12"
               }))
            await Created(check);

        var report = await Get(env, "cash-flow?granularity=week&periods=4&from=2026-03-12");

        // Açılış kasada gerçekleşmiş 300; gelecekteki tek nakit çekin 100'üdür. Avanslar vade satırı olarak ne giriş ne çıkış sayılır.
        report.GetProperty("opening").GetDecimal().ShouldBe(300m);
        foreach (var period in report.GetProperty("periods").EnumerateArray()) {
            period.GetProperty("inflow").GetProperty("dueLines").GetDecimal().ShouldBe(0m);
            period.GetProperty("outflow").GetProperty("dueLines").GetDecimal().ShouldBe(0m);
        }

        report.GetProperty("periods")[1].GetProperty("inflow").GetProperty("instruments").GetDecimal().ShouldBe(100m);
        report.GetProperty("closing").GetDecimal().ShouldBe(400m);
    }

    [Test]
    public async Task CashFlow_OverdueFirstPeriod_BeyondHorizonDropped() {
        var env = await ReportEnv();
        await DueOn(env, 120m, "2026-02-11"); // vadesi geçmiş: ilk dönem
        await DueOn(env, 240m, "2026-03-14"); // gün 3
        await DueOn(env, 360m, "2026-06-01"); // ufuk dışı

        var report  = await Get(env, "cash-flow?granularity=day&periods=5&from=2026-03-12");
        var periods = report.GetProperty("periods").EnumerateArray().ToList();

        periods.Count.ShouldBe(5);
        periods[0].GetProperty("inflow").GetProperty("dueLines").GetDecimal().ShouldBe(120m);
        periods[2].GetProperty("inflow").GetProperty("dueLines").GetDecimal().ShouldBe(240m);
        report.GetProperty("closing").GetDecimal().ShouldBe(360m);
    }

    [Test]
    public async Task CashFlow_ForeignBalancesConvertedAtTodayRate() {
        var env = await ReportEnv();
        var tl  = await Account(env, "Kasa");
        var usd = await Account(env, "USD Banka", "bank", "USD");
        await TreasuryTestSupport.AddMovementAsync(env.Tenant, tl, 500m, Today);
        await TreasuryTestSupport.AddMovementAsync(env.Tenant, usd, 100m, Today, "USD");
        await PostingSupport.Override(env, "USD", "2026-03-12", 32.5m);

        var report = await Get(env, "cash-flow?granularity=day&periods=1&from=2026-03-12");

        report.GetProperty("opening").GetDecimal().ShouldBe(3750m); // 500 + 100 x 32,5
        report.GetProperty("currency").GetString().ShouldBe("TRY");
    }

    [Test]
    public async Task CashFlow_InvalidPeriods_Return400() {
        var env = await ReportEnv();

        using var weeks = await env.Client.GetAsync($"{Base}/reports/cash-flow?granularity=week&periods=27");
        weeks.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var days = await env.Client.GetAsync($"{Base}/reports/cash-flow?granularity=day&periods=91");
        days.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var kind = await env.Client.GetAsync($"{Base}/reports/cash-flow?granularity=month");
        kind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var ok = await env.Client.GetAsync($"{Base}/reports/cash-flow?granularity=week&periods=26&from=2026-03-12");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Reports_RequireFinansRead() {
        var env = await ReportEnv();
        using var limited = await OtherClient(env, "cari.read");

        using var aging = await limited.GetAsync($"{Base}/reports/aging?direction=receivable");
        aging.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var flow = await limited.GetAsync($"{Base}/reports/cash-flow?granularity=day");
        flow.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
