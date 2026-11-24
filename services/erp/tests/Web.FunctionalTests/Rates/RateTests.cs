using ERP.Domain.Invoicing;
using ERP.Infrastructure.Rates;

namespace ERP.Web.FunctionalTests.Rates;

public class RateTests : TestBase {
    private const           string Url     = "/api/erp/exchange-rates";
    private static readonly Guid   TenantA = Guid.CreateVersion7();
    private static readonly Guid   TenantB = Guid.CreateVersion7();

    private static readonly DateOnly Friday = new(2026, 11, 20);
    private static readonly DateOnly Sunday = new(2026, 11, 22);

    [SetUp]
    public void ResetFake() {
        Factory.Tcmb.Reset();
    }

    private static Task<HttpClient> Client(Guid tenant, params string[] permissions) {
        return Factory.ClientAsync(tenant, null, null, null, permissions);
    }

    private static async Task LoadFriday() {
        Factory.Tcmb.PublishToday("tcmb-2026-11-20.xml");
        using var admin = await Client(TenantA, "kur.manage");
        using var fetch = await admin.PostAsync($"{Url}/fetch", null);
        fetch.StatusCode.ShouldBe(HttpStatusCode.OK, await fetch.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> Lookup(HttpClient client, string currency, DateOnly date, string type = "ForexBuying") {
        return client.GetAsync($"{Url}/lookup?currency={currency}&date={date:yyyy-MM-dd}&type={type}");
    }

    [Test]
    public void ParseTcmbXml_UsdEurGbp() {
        var rows = TcmbClient.Parse(FakeTcmbSource.Fixture("tcmb-2026-11-20.xml"));

        rows.Select(r => r.Currency).Distinct().Order().ToArray().ShouldBe(["EUR", "GBP", "USD"]);
        rows.ShouldAllBe(r => r.Date == Friday && r.Source == "tcmb");
        Rate(rows, "USD", RateType.ForexBuying).ShouldBe(34.1m);
        Rate(rows, "USD", RateType.ForexSelling).ShouldBe(34.16m);
        Rate(rows, "USD", RateType.BanknoteBuying).ShouldBe(33.9m);
        Rate(rows, "EUR", RateType.BanknoteSelling).ShouldBe(39.7m);
        Rate(rows, "GBP", RateType.ForexBuying).ShouldBe(45m);
        rows.Count.ShouldBe(12);
    }

    [Test]
    public async Task TcmbClient_UsesArchiveUrlAndTreatsMissingBulletinAsNone() {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("20112026.xml")
                                                     ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FakeTcmbSource.Fixture("tcmb-2026-11-20.xml")) }
                                                     : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new TcmbClient(new HttpClient(handler));

        var friday = await client.FetchAsync(Friday, CancellationToken.None);
        var sunday = await client.FetchAsync(Sunday, CancellationToken.None);
        await client.FetchAsync(null, CancellationToken.None);

        friday.ShouldNotBeNull().Count.ShouldBe(12);
        sunday.ShouldBeNull();
        handler.Urls.ShouldBe(["https://www.tcmb.gov.tr/kurlar/202611/20112026.xml", "https://www.tcmb.gov.tr/kurlar/202611/22112026.xml",
                               "https://www.tcmb.gov.tr/kurlar/today.xml"]);
    }

    [Test]
    public async Task Rate_SundayUsesFriday_NoneWithin10Days_409() {
        await LoadFriday();
        using var client = await Client(TenantA, "fatura.read");

        using var sunday = await Lookup(client, "USD", Sunday);
        sunday.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await sunday.JsonAsync();
        body.GetProperty("rate").GetDecimal().ShouldBe(34.1m);
        body.GetProperty("effectiveDate").GetString().ShouldBe("2026-11-20");

        // 10 gün geriye kadar bakılır: Cuma'dan 10 gün sonrası geçerli, 11 gün sonrası değil.
        using var tenDays = await Lookup(client, "USD", Friday.AddDays(10));
        tenDays.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var elevenDays = await Lookup(client, "USD", Friday.AddDays(11));
        elevenDays.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await elevenDays.ErrorCodeAsync()).ShouldBe("rate_missing");

        using var beforeAny = await Lookup(client, "EUR", Friday.AddDays(-3));
        beforeAny.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task TRY_AlwaysOne() {
        using var client = await Client(TenantA, "fatura.read");

        using var response = await Lookup(client, "TRY", Sunday);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("rate").GetDecimal().ShouldBe(1m);
        body.GetProperty("effectiveDate").GetString().ShouldBe("2026-11-22");
    }

    [Test]
    public async Task TenantOverride_WinsForThatTenantOnly() {
        await LoadFriday();
        using var a = await Client(TenantA, "fatura.read", "kur.manage");
        using var b = await Client(TenantB, "fatura.read", "kur.manage");

        using var put = await a.PutAsJsonAsync($"{Url}/override", new { date = Sunday.ToString("yyyy-MM-dd"), currency = "USD", type = "ForexBuying", rate = 35.5m });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        // İkinci kez yazmak günceller.
        using var again = await a.PutAsJsonAsync($"{Url}/override", new { date = Sunday.ToString("yyyy-MM-dd"), currency = "usd", type = "ForexBuying", rate = 35.75m });
        again.StatusCode.ShouldBe(HttpStatusCode.OK);

        var forA = await (await Lookup(a, "USD", Sunday)).JsonAsync();
        forA.GetProperty("rate").GetDecimal().ShouldBe(35.75m);
        forA.GetProperty("overridden").GetBoolean().ShouldBeTrue();
        forA.GetProperty("effectiveDate").GetString().ShouldBe("2026-11-22");
        var forB = await (await Lookup(b, "USD", Sunday)).JsonAsync();
        forB.GetProperty("rate").GetDecimal().ShouldBe(34.1m);
        forB.GetProperty("overridden").GetBoolean().ShouldBeFalse();

        // Liste elle kuru işaretler; başka kur türü etkilenmez.
        var list = await (await a.GetAsync($"{Url}?date={Sunday:yyyy-MM-dd}")).JsonAsync();
        var usdBuying = list.EnumerateArray().Single(r => r.GetProperty("currency").GetString() == "USD" && r.GetProperty("type").GetString() == "forexBuying");
        usdBuying.GetProperty("overridden").GetBoolean().ShouldBeTrue();
        var usdSelling = list.EnumerateArray().Single(r => r.GetProperty("currency").GetString() == "USD" && r.GetProperty("type").GetString() == "forexSelling");
        usdSelling.GetProperty("overridden").GetBoolean().ShouldBeFalse();
        usdSelling.GetProperty("rate").GetDecimal().ShouldBe(34.16m);
    }

    [Test]
    public async Task Override_RequiresKurManage() {
        using var reader = await Client(TenantA, "fatura.read");

        using var put   = await reader.PutAsJsonAsync($"{Url}/override", new { date = "2026-11-20", currency = "USD", type = "ForexBuying", rate = 35m });
        using var fetch = await reader.PostAsync($"{Url}/fetch", null);
        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fetch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Factory.Tcmb.Requests.ShouldBeEmpty();

        using var manager = await Client(TenantA, "kur.manage");
        using var bad     = await manager.PutAsJsonAsync($"{Url}/override", new { date = "2026-11-20", currency = "USD", type = "ForexBuying", rate = 0m });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var unknown = await manager.PutAsJsonAsync($"{Url}/override", new { date = "2026-11-20", currency = "JPY", type = "ForexBuying", rate = 1m });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Fetch_BackfillsMissingWorkdaysAndIsIdempotent() {
        Factory.Tcmb.PublishToday("tcmb-2026-11-23.xml");
        Factory.Tcmb.Publish(Friday, "tcmb-2026-11-20.xml");
        using var admin = await Client(TenantA, "kur.manage", "fatura.read");

        var first = await (await admin.PostAsync($"{Url}/fetch", null)).JsonAsync();
        first.GetProperty("failed").GetInt32().ShouldBe(0);
        first.GetProperty("stored").GetInt32().ShouldBeGreaterThanOrEqualTo(12);
        var second = await (await admin.PostAsync($"{Url}/fetch", null)).JsonAsync();
        second.GetProperty("stored").GetInt32().ShouldBe(0);

        var monday = await (await Lookup(admin, "USD", new DateOnly(2026, 11, 23))).JsonAsync();
        monday.GetProperty("rate").GetDecimal().ShouldBe(34.2m);
    }

    private static decimal Rate(IEnumerable<ExchangeRate> rows, string currency, RateType type) {
        return rows.Single(r => r.Currency == currency && r.Type == type).Rate;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }
}
