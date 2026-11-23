using System.Net.Http.Json;
using System.Text.Json;

namespace ERP.Web.FunctionalTests.Settings;

public class SettingsTests : TestBase {
    private const           string Firma  = "/api/erp/settings/firma";
    private const           string Genel  = "/api/erp/settings/genel";
    private const           string Audit  = "/api/erp/audit";
    private const           string Iban   = "TR330006100519786457841326";
    private static readonly Guid   Tenant = Guid.CreateVersion7();

    private static Task<HttpClient> Admin(Guid? tenant = null) {
        return Factory.ClientAsync(tenant ?? Tenant,
                                   permissions: ["ayarlar.firma", "ayarlar.genel", "ayarlar.finans", "denetim.read"]);
    }

    private static object Profile(string? taxNumber = "1234567890", string iban = Iban, string legalName = "Örnek A.Ş.") {
        return new {
                       legalName,
                       taxNumber,
                       taxOffice = "Kadıköy",
                       ibans     = new[] { new { bankName = "Banka", iban, currency = "TRY" } }
                   };
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, int version, object value, string url = Firma) {
        return client.PutAsJsonAsync(url, new { version, value });
    }

    [Test]
    public async Task GetCompanyProfile_Default_ReturnsEmptyVersion0() {
        using var client = await Admin();

        using var response = await client.GetAsync(Firma);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("version").GetInt32().ShouldBe(0);
        var value = body.GetProperty("value");
        value.GetProperty("legalName").ValueKind.ShouldBe(JsonValueKind.Null);
        value.GetProperty("ibans").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task PutCompanyProfile_Valid_StoresNormalizedAndIncrementsVersion() {
        using var client = await Admin();

        using var first = await Put(client, 0, Profile(iban: "tr33 0006 1005 1978 6457 8413 26"));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(1);

        using var second = await Put(client, 1, Profile(legalName: "Yeni Ad"));
        (await second.JsonAsync()).GetProperty("version").GetInt32().ShouldBe(2);

        var body = await (await client.GetAsync(Firma)).JsonAsync();
        body.GetProperty("version").GetInt32().ShouldBe(2);
        body.GetProperty("value").GetProperty("legalName").GetString().ShouldBe("Yeni Ad");
        body.GetProperty("value").GetProperty("ibans")[0].GetProperty("iban").GetString().ShouldBe(Iban);
    }

    [Test]
    public async Task PutCompanyProfile_InvalidIban_Returns400() {
        using var client = await Admin();

        using var response = await Put(client, 0, Profile(iban: "TR330006100519786457841327"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutCompanyProfile_InvalidVkn_Returns400() {
        using var client = await Admin();

        using var response = await Put(client, 0, Profile(taxNumber: "1234567891"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PutCompanyProfile_ValidTckn_Returns200() {
        using var client = await Admin();

        using var response = await Put(client, 0, Profile(taxNumber: "10000000146"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task PutGeneral_InvalidValues_Returns400() {
        using var client = await Admin();

        (await Put(client, 0, new { defaultCurrency = "JPY" }, Genel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { quantityDecimals = 5 }, Genel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { unitPriceDecimals = 1 }, Genel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { quantityDecimals = 3, unitPriceDecimals = 4, defaultCurrency = "USD" }, Genel))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetGeneral_Default_HasDefaultDateFormat() {
        using var client = await Admin();

        var body = await (await client.GetAsync(Genel)).JsonAsync();

        body.GetProperty("value").GetProperty("dateFormat").GetString().ShouldBe("dd.MM.yyyy");
    }

    [Test]
    public async Task PutGeneral_DateFormat_ValidatesAllowedList() {
        using var client = await Admin();

        (await Put(client, 0, new { dateFormat = "MM/dd/yyyy" }, Genel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Put(client, 0, new { dateFormat = "yyyy-MM-dd" }, Genel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Put(client, 1, new { dateFormat = "dd/MM/yyyy" }, Genel)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await (await client.GetAsync(Genel)).JsonAsync();
        body.GetProperty("value").GetProperty("dateFormat").GetString().ShouldBe("dd/MM/yyyy");
    }

    [Test]
    public async Task SetupCategory_RoundTrips() {
        using var client = await Admin();
        const string url = "/api/erp/settings/kurulum";

        var initial = await (await client.GetAsync(url)).JsonAsync();
        initial.GetProperty("version").GetInt32().ShouldBe(0);
        initial.GetProperty("value").GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        using var put = await Put(client, 0, new { sector = "perakende", completedAt = "2026-11-22T10:00:00+00:00" }, url);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await (await client.GetAsync(url)).JsonAsync();
        body.GetProperty("version").GetInt32().ShouldBe(1);
        body.GetProperty("value").GetProperty("sector").GetString().ShouldBe("perakende");
        body.GetProperty("value").GetProperty("completedAt").GetString().ShouldNotBeNull();
        body.GetProperty("value").GetProperty("dismissedAt").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Put_StaleVersion_Returns409VersionConflict() {
        using var client = await Admin();
        (await Put(client, 0, Profile())).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await Put(client, 0, Profile(legalName: "Eski sürüm"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("version_conflict");
    }

    [Test]
    public async Task Put_ConcurrentSameVersion_OnlyOneSucceeds() {
        using var client = await Admin();

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
                                             .Select(i => Put(client, 0, Profile(legalName: $"Firma {i}"))));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(5);
    }

    [Test]
    public async Task Put_WritesAuditEntry() {
        using var client = await Admin();
        await Put(client, 0, Profile());

        var body = await (await client.GetAsync(Audit)).JsonAsync();

        body.GetProperty("total").GetInt32().ShouldBe(1);
        var item = body.GetProperty("items")[0];
        item.GetProperty("action").GetString().ShouldBe("settings.updated");
        item.GetProperty("entityType").GetString().ShouldBe("settings");
        item.GetProperty("entityId").GetString().ShouldBe("firma");
        item.GetProperty("actorLabel").GetString().ShouldNotBeNullOrWhiteSpace();
        item.GetProperty("before").GetString().ShouldNotBeNullOrWhiteSpace();
        item.GetProperty("after").GetString()!.ShouldContain("Örnek");
    }

    [Test]
    public async Task Get_WithoutPermission_Returns403() {
        using var client = await Factory.ClientAsync(Tenant, permissions: ["ayarlar.genel"]);

        (await client.GetAsync(Firma)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Put(client, 0, Profile())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(Genel)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Settings_OtherTenantInvisible() {
        using var a = await Admin();
        using var b = await Admin(Guid.CreateVersion7());
        await Put(a, 0, Profile());

        var body = await (await b.GetAsync(Firma)).JsonAsync();

        body.GetProperty("version").GetInt32().ShouldBe(0);
        body.GetProperty("value").GetProperty("legalName").ValueKind.ShouldBe(JsonValueKind.Null);
        (await (await b.GetAsync(Audit)).JsonAsync()).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Test]
    public async Task UnknownCategory_Returns404() {
        using var client = await Admin();

        (await client.GetAsync("/api/erp/settings/yok")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Put(client, 0, new { }, "/api/erp/settings/yok")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Audit_ListsSettingsChangesNewestFirst() {
        using var client = await Admin();
        await Put(client, 0, Profile());
        await Put(client, 0, new { defaultCurrency = "USD" }, Genel);
        await Put(client, 1, Profile(legalName: "İkinci"));

        var body = await (await client.GetAsync(Audit)).JsonAsync();

        body.GetProperty("total").GetInt32().ShouldBe(3);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("entityId").GetString())
            .ToList();
        ids.ShouldBe(["firma", "genel", "firma"]);

        var paged = await (await client.GetAsync($"{Audit}?entityType=settings&pageSize=1&page=2")).JsonAsync();
        paged.GetProperty("total").GetInt32().ShouldBe(3);
        paged.GetProperty("items").GetArrayLength().ShouldBe(1);
        (await client.GetAsync($"{Audit}?entityType=baska")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{Audit}?pageSize=201")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Audit_RequiresDenetimRead() {
        using var client = await Factory.ClientAsync(Tenant, permissions: ["ayarlar.firma"]);

        (await client.GetAsync(Audit)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
