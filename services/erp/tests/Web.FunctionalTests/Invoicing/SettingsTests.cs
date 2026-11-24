namespace ERP.Web.FunctionalTests.Invoicing;

public class SettingsTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static Task<HttpClient> Admin() {
        return Factory.ClientAsync(Tenant, null, null, null, ["ayarlar.ebelge", "ayarlar.belgeler", "ayarlar.finans"]);
    }

    [Test]
    public async Task EBelgeSettings_RoundTrip() {
        using var client = await Admin();

        var defaults = (await (await client.GetAsync("/api/erp/settings/ebelge")).JsonAsync()).GetProperty("value");
        defaults.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        defaults.GetProperty("defaultScenario").GetString().ShouldBe("basic");
        defaults.GetProperty("eIrsaliyeEnabled").GetBoolean().ShouldBeFalse();

        using var put = await client.PutAsJsonAsync("/api/erp/settings/ebelge",
                                                    new { version = 0, value = new { enabled = true, defaultScenario = "commercial", eIrsaliyeEnabled = true } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await (await client.GetAsync("/api/erp/settings/ebelge")).JsonAsync();
        body.GetProperty("version").GetInt32().ShouldBe(1);
        body.GetProperty("value").GetProperty("enabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("value").GetProperty("defaultScenario").GetString().ShouldBe("commercial");
        body.GetProperty("value").GetProperty("eIrsaliyeEnabled").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task EBelgeSettings_RequiresPermission() {
        using var client = await Factory.ClientAsync(Tenant, null, null, null, ["ayarlar.belgeler"]);

        (await client.GetAsync("/api/erp/settings/ebelge")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DocumentSettings_DefaultsRoundTripAndValidation() {
        using var client = await Admin();

        var defaults = (await (await client.GetAsync("/api/erp/settings/belgeler")).JsonAsync()).GetProperty("value");
        defaults.GetProperty("accentColor").GetString().ShouldBe("#5D87FF");
        defaults.GetProperty("showIbans").GetBoolean().ShouldBeTrue();
        defaults.GetProperty("invoiceEmailSubject").GetString().ShouldBe("{firma} - {belgeNo} numaralı fatura");

        using var bad = await client.PutAsJsonAsync("/api/erp/settings/belgeler",
                                                    new { version = 0, value = new { accentColor = "mavi", showIbans = true, invoiceEmailSubject = "x", invoiceEmailBody = "y" } });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var ok = await client.PutAsJsonAsync("/api/erp/settings/belgeler",
                                                   new { version = 0, value = new { accentColor = "#112233", footerNote = "Teşekkürler", showIbans = false, invoiceEmailSubject = "Konu", invoiceEmailBody = "Metin" } });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var value = (await (await client.GetAsync("/api/erp/settings/belgeler")).JsonAsync()).GetProperty("value");
        value.GetProperty("footerNote").GetString().ShouldBe("Teşekkürler");
        value.GetProperty("showIbans").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task FinanceRateType_DefaultForexBuying() {
        using var client = await Admin();

        var defaults = (await (await client.GetAsync("/api/erp/settings/finans")).JsonAsync()).GetProperty("value");
        defaults.GetProperty("rateType").GetString().ShouldBe("forexBuying");

        // Eski gövde (rateType yok) varsayılanı korur; yeni değer saklanır.
        using var legacy = await client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { periodLockDate = (string?)null } });
        legacy.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync("/api/erp/settings/finans")).JsonAsync()).GetProperty("value").GetProperty("rateType").GetString().ShouldBe("forexBuying");

        using var put = await client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 1, value = new { periodLockDate = (string?)null, rateType = "banknoteSelling" } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await client.GetAsync("/api/erp/settings/finans")).JsonAsync()).GetProperty("value").GetProperty("rateType").GetString().ShouldBe("banknoteSelling");

        using var invalid = await client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 2, value = new { periodLockDate = (string?)null, rateType = "nonsense" } });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
