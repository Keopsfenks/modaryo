using System.Text.Json;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class ImportFileTests : TestBase {
    private static readonly string[] Modules = ["ithalat"];

    private static async Task<JsonElement> CreateFile(Env env) {
        return await Post(env.Client, "import-files", new { supplierId = env.Supplier, originCountry = "Çin", incoterm = "FOB", currency = "USD", notes = "Deneme" }, HttpStatusCode.Created);
    }

    [Test]
    public async Task ImportFile_SummaryLandedUnitCost() {
        var s       = await InvoiceMatchTests.Received(100m, 10m, modules: Modules);
        var env     = s.Env;
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        var file    = await CreateFile(env);
        var id      = file.GetProperty("id").GetGuid();
        file.GetProperty("number").GetString().ShouldBe("IT" + DateTime.UtcNow.AddHours(3).Year + "0001");
        await Post(env.Client, $"import-files/{id}/link", new { kind = "receipt", id = receipt });
        await Post(env.Client, $"import-files/{id}/link", new { kind = "po", id = s.Po });
        var landed = await LandedCostTests.CreateLanded(env, "amount", [], [LandedCostTests.Manual(150m, "navlun"), LandedCostTests.Manual(50m, "gumruk")], id);
        await Post(env.Client, $"landed-costs/{landed.GetProperty("id").GetGuid()}/post");

        using var response = await env.Client.GetAsync($"{Api}/import-files/{id}/summary");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var summary = await response.JsonAsync();
        summary.GetProperty("goodsValueTl").GetDecimal().ShouldBe(1000m);
        summary.GetProperty("totalCostTl").GetDecimal().ShouldBe(200m);
        summary.GetProperty("costsByType").EnumerateArray().ToDictionary(c => c.GetProperty("costType").GetString()!, c => c.GetProperty("amountTl").GetDecimal())
               .ShouldBe(new Dictionary<string, decimal> { ["gumruk"] = 50m, ["navlun"] = 150m });
        var item = summary.GetProperty("items")[0];
        item.GetProperty("landedUnitCostTl").GetDecimal().ShouldBe(12m);
        item.GetProperty("baseQuantity").GetDecimal().ShouldBe(100m);
    }

    [Test]
    public async Task ImportFile_LinkTwiceAndClose_Conflicts() {
        var s       = await InvoiceMatchTests.Received(10m, 10m, modules: Modules);
        var env     = s.Env;
        var id      = (await CreateFile(env)).GetProperty("id").GetGuid();
        var receipt = s.Receipt.GetProperty("id").GetGuid();
        await Post(env.Client, $"import-files/{id}/link", new { kind = "receipt", id = receipt });

        using var twice = await env.Client.PostAsJsonAsync($"{Api}/import-files/{id}/link", new { kind = "receipt", id = receipt });
        var closed = await Post(env.Client, $"import-files/{id}/close");
        using var late   = await env.Client.PostAsJsonAsync($"{Api}/import-files/{id}/link", new { kind = "po", id = s.Po });
        using var landed = await env.Client.PostAsJsonAsync($"{Api}/landed-costs", new { date = Day.ToString("yyyy-MM-dd"), key = "amount", importFileId = id, sources = new[] { LandedCostTests.Manual(10m) } });

        await Fail(twice, HttpStatusCode.Conflict, "already_linked");
        closed.GetProperty("status").GetString().ShouldBe("closed");
        await Fail(late, HttpStatusCode.Conflict, "import_file_closed");
        await Fail(landed, HttpStatusCode.Conflict, "import_file_closed");
        var detail = await (await env.Client.GetAsync($"{Api}/import-files/{id}")).JsonAsync();
        detail.GetProperty("links")[0].GetProperty("label").GetString().ShouldBe(s.Receipt.GetProperty("number").GetString());
    }

    [Test]
    public async Task ImportFileWithoutModule_Returns403() {
        var env = await NewPurchasingEnv();

        using var create = await env.Client.PostAsJsonAsync($"{Api}/import-files", new { supplierId = env.Supplier, originCountry = "Çin", incoterm = "FOB", currency = "USD" });
        using var list   = await env.Client.GetAsync($"{Api}/import-files");

        await Fail(create, HttpStatusCode.Forbidden, "module_disabled");
        await Fail(list, HttpStatusCode.Forbidden, "module_disabled");
    }
}
