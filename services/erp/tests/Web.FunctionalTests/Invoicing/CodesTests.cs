using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class CodesTests : TestBase {
    [Test]
    public async Task Codes_ListsSeededWithholdingAndExemption_AndNeedsFaturaRead() {
        var env = await NewEnv();

        using var response = await env.Client.GetAsync($"{Url}/codes");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        var withholding = body.GetProperty("withholding").EnumerateArray().ToList();
        withholding.Count.ShouldBeGreaterThan(0);
        withholding[0].GetProperty("tenths").GetInt32().ShouldBeInRange(1, 10);
        body.GetProperty("exemption").GetArrayLength().ShouldBeGreaterThan(0);

        using var denied = await (await OtherClient(env, "cari.read")).GetAsync($"{Url}/codes");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
