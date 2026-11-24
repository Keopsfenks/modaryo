using static ERP.Web.FunctionalTests.Treasury.TreasuryTestSupport;

namespace ERP.Web.FunctionalTests.Treasury;

public class SettingsTests : TestBase {
    private static readonly Dictionary<string, string> TekDuzen = new() {
        ["customers"] = "120", ["suppliers"] = "320", ["employees"] = "335", ["advancesGiven"] = "159", ["advancesReceived"] = "340",
        ["cash"] = "100", ["bank"] = "102", ["checksReceived"] = "101", ["checksIssued"] = "103", ["notesReceived"] = "121",
        ["notesPayable"] = "321", ["sales"] = "600", ["salesReturns"] = "610", ["salesDiscounts"] = "611", ["purchasesInventory"] = "153",
        ["vatOutput"] = "391", ["vatInput"] = "191", ["withholdingVatPayable"] = "360", ["fxGain"] = "646", ["fxLoss"] = "656", ["expenseDefault"] = "770"
    };

    [Test]
    public async Task MappingDefaults_TekDuzen() {
        var env = await NewEnv();

        var value = (await (await env.Client.GetAsync("/api/erp/settings/finans")).JsonAsync()).GetProperty("value");

        var mapping = value.GetProperty("accountMapping");
        foreach (var (key, code) in TekDuzen) mapping.GetProperty(key).GetString().ShouldBe(code, key);
        value.GetProperty("subAccountPerCari").GetBoolean().ShouldBeFalse();
        value.GetProperty("expenseCategoryCodes").GetProperty("kur-farki").GetString().ShouldBe("656");
        value.GetProperty("rateType").GetString().ShouldBe("forexBuying");
    }

    [Test]
    public async Task MappingOverride_RoundTripAndValidation() {
        var env = await NewEnv();

        using var bad = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { accountMapping = new { customers = "abc" } } });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var ok = await env.Client.PutAsJsonAsync("/api/erp/settings/finans",
                                                       new { version = 0, value = new { subAccountPerCari = true, accountMapping = new { customers = "120.01", cash = " " }, expenseCategoryCodes = new Dictionary<string, string> { ["kira"] = "770.05" } } });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());

        var value = (await (await env.Client.GetAsync("/api/erp/settings/finans")).JsonAsync()).GetProperty("value");
        value.GetProperty("subAccountPerCari").GetBoolean().ShouldBeTrue();
        value.GetProperty("accountMapping").GetProperty("customers").GetString().ShouldBe("120.01");
        value.GetProperty("accountMapping").GetProperty("cash").GetString().ShouldBe("100");
        value.GetProperty("accountMapping").GetProperty("suppliers").GetString().ShouldBe("320");
        value.GetProperty("expenseCategoryCodes").GetProperty("kira").GetString().ShouldBe("770.05");
        value.GetProperty("expenseCategoryCodes").GetProperty("kur-farki").GetString().ShouldBe("656");
    }
}
