
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class SettingsTests : TestBase {
    [Test]
    public async Task Settings_Defaults() {
        var env      = await NewEnv();
        var settings = (await GetSettings(env.Client)).GetProperty("value");

        settings.GetProperty("legalDocumentMode").GetString().ShouldBe("fiscalReceipt");
        settings.GetProperty("scaleBarcodes").GetArrayLength().ShouldBe(0);
        settings.GetProperty("allowNegativeCashWarningOnly").GetBoolean().ShouldBeTrue();
        settings.GetProperty("requireCustomerForVeresiye").GetBoolean().ShouldBeTrue();
        settings.TryGetProperty("scales", out _).ShouldBeFalse();
        settings.TryGetProperty("scaleFormats", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Settings_EArchiveRequiresEBelge() {
        var env = await NewEnv();

        using var refused = await PutSettings(env.Client, new { legalDocumentMode = "eArchiveInvoice" });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await refused.ErrorCodeAsync()).ShouldBe("ebelge_required");

        await Invoicing.InvoiceTestSupport.EnableEBelge(env.Client);
        using var accepted = await PutSettings(env.Client, new { legalDocumentMode = "eArchiveInvoice" });
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        (await GetSettings(env.Client)).GetProperty("value").GetProperty("legalDocumentMode").GetString().ShouldBe("eArchiveInvoice");

        // Fiş moduna dönmek e-Belge gerektirmez.
        using var back = await PutSettings(env.Client, new { legalDocumentMode = "fiscalReceipt" });
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Settings_ScaleFormatsValidated() {
        var env = await NewEnv();

        using var duplicate = await PutSettings(env.Client, new {
            scaleBarcodes = new object[] {
                new { prefix = "27", itemCodeLength = 5, valueLength = 5, valueKind = "weightGrams" },
                new { prefix = "27", itemCodeLength = 5, valueLength = 5, valueKind = "priceKurus" }
            }
        });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await duplicate.Content.ReadAsStringAsync());

        using var wrongLength = await PutSettings(env.Client, new { scaleBarcodes = new object[] { new { prefix = "27", itemCodeLength = 6, valueLength = 5, valueKind = "weightGrams" } } });
        wrongLength.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await wrongLength.Content.ReadAsStringAsync());

        using var letters = await PutSettings(env.Client, new { scaleBarcodes = new object[] { new { prefix = "2A", itemCodeLength = 5, valueLength = 5, valueKind = "weightGrams" } } });
        letters.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await letters.Content.ReadAsStringAsync());

        await SetScaleFormats(env.Client);
        (await GetSettings(env.Client)).GetProperty("value").GetProperty("scaleBarcodes").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task RetailCustomerAutoCreatedAndUndeletable() {
        var env      = await NewEnv();
        var settings = (await GetSettings(env.Client)).GetProperty("value");
        var retail   = settings.GetProperty("retailCustomerId").GetGuid();

        using var card = await env.Client.GetAsync($"/api/erp/current-accounts/{retail}");
        card.StatusCode.ShouldBe(HttpStatusCode.OK, await card.Content.ReadAsStringAsync());
        var body = await card.JsonAsync();
        body.GetProperty("name").GetString().ShouldBe("Perakende Müşteri");
        body.GetProperty("isCustomer").GetBoolean().ShouldBeTrue();

        // İkinci erişimde yeni cari açılmaz.
        (await GetSettings(env.Client)).GetProperty("value").GetProperty("retailCustomerId").GetGuid().ShouldBe(retail);

        using var delete = await env.Client.DeleteAsync($"/api/erp/current-accounts/{retail}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict, await delete.Content.ReadAsStringAsync());
        (await delete.ErrorCodeAsync()).ShouldBe("retail_customer_protected");

        // İstemci perakende müşteriyi değiştiremez.
        using var tamper = await PutSettings(env.Client, new { retailCustomerId = Guid.NewGuid() });
        tamper.StatusCode.ShouldBe(HttpStatusCode.OK, await tamper.Content.ReadAsStringAsync());
        (await GetSettings(env.Client)).GetProperty("value").GetProperty("retailCustomerId").GetGuid().ShouldBe(retail);
    }

    [Test]
    public async Task RetailCustomer_CreatedWhenSettingsSavedFirst() {
        var env = await NewEnv();

        // Ayar kaydı önce yazılırsa (müşteri henüz yok) sonraki erişim müşteriyi tamamlar.
        using var put = await env.Client.PutAsJsonAsync("/api/erp/settings/pos", new { version = 0, value = new { allowNegativeCashWarningOnly = false } });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var settings = (await GetSettings(env.Client)).GetProperty("value");
        settings.GetProperty("retailCustomerId").GetGuid().ShouldNotBe(Guid.Empty);
        settings.GetProperty("allowNegativeCashWarningOnly").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task ModuleDisabled_Returns403() {
        var env      = await NewEnv(modules: ["sube"]);
        var expected = HttpStatusCode.Forbidden;

        using var registers = await env.Client.GetAsync($"{Api}/registers");
        registers.StatusCode.ShouldBe(expected, await registers.Content.ReadAsStringAsync());
        (await registers.ErrorCodeAsync()).ShouldBe("module_disabled");

        using var create = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env));
        (await create.ErrorCodeAsync()).ShouldBe("module_disabled");

        using var lookup = await env.Client.GetAsync($"{Api}/lookup?code=X");
        (await lookup.ErrorCodeAsync()).ShouldBe("module_disabled");

        using var settings = await env.Client.GetAsync("/api/erp/settings/pos");
        settings.StatusCode.ShouldBe(expected, await settings.Content.ReadAsStringAsync());
        (await settings.ErrorCodeAsync()).ShouldBe("module_disabled");
    }

    [Test]
    public async Task Settings_RequirePermission() {
        var env    = await NewEnv();
        var plain  = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["pos.sell"]);

        using var response = await plain.GetAsync("/api/erp/settings/pos");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
