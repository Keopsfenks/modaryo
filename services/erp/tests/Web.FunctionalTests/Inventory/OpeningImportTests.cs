using System.Text.Json;
using ClosedXML.Excel;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class OpeningImportTests : TestBase {
    private const string Preview = Base + "/opening-import/preview";

    private static readonly string[] Headers = ["Depo kodu", "Ürün kodu", "Varyant kodu", "Miktar", "Birim", "Birim maliyet", "Lot", "SKT", "Seri"];

    private static byte[] Sheet(params string[][] rows) {
        using var workbook = new XLWorkbook();
        var       sheet    = workbook.AddWorksheet("Açılış");
        for (var c = 0; c < Headers.Length; c++) sheet.Cell(1, c + 1).Value = Headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                if (rows[r][c].Length > 0)
                    sheet.Cell(r + 2, c + 1).Value = rows[r][c];

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string[] R(string warehouse, string product, string quantity, string cost = "10", string variant = "", string unit = "",
                              string lot = "", string expiry = "", string serial = "") {
        return [warehouse, product, variant, quantity, unit, cost, lot, expiry, serial];
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] file) {
        using var form    = new MultipartFormDataContent();
        var       content = new ByteArrayContent(file);
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "acilis.xlsx");
        return await client.PostAsync(Preview, form);
    }

    private static async Task<JsonElement> PreviewOk(HttpClient client, byte[] file) {
        using var response = await Upload(client, file);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> Commit(HttpClient client, JsonElement preview) {
        return client.PostAsync($"{Base}/opening-import/{preview.GetProperty("batchId").GetGuid()}/commit", null);
    }

    private static async Task<string> WarehouseCode(Env env) {
        using var response = await env.Client.GetAsync($"{Base}/warehouses/default?branchId={env.Branch}");
        return (await response.JsonAsync()).GetProperty("code").GetString()!;
    }

    [Test]
    public async Task OpeningImport_PreviewAndCommit() {
        var env    = await NewEnv();
        var wh     = await WarehouseCode(env);
        var second = await CreateProduct(env.Client, "P2");

        var preview = await PreviewOk(env.Client, Sheet(R(wh, "P1", "5", "10"), R(wh, "P2", "2", "20")));
        preview.GetProperty("errors").GetInt32().ShouldBe(0);
        preview.GetProperty("creates").GetInt32().ShouldBe(2);
        (await Movements(env.Tenant)).ShouldBeEmpty(); // önizleme hiçbir şey yazmaz

        using var commit = await Commit(env.Client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        var movements = await Movements(env.Tenant);
        movements.Count.ShouldBe(2);
        movements.ShouldAllBe(m => m.Type.ToString() == "Opening");
        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(5m);
        (await BalanceOf(env, env.Warehouse, second)).GetProperty("averageCost").GetDecimal().ShouldBe(20m);

        using var again = await Commit(env.Client, preview);
        await Fail(again, HttpStatusCode.Conflict, "import_already_committed");
        (await Movements(env.Tenant)).Count.ShouldBe(2);

        using var docs = await env.Client.GetAsync($"{Base}/documents?kind=opening");
        (await docs.JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task OpeningImport_UnknownWarehouse_RowError() {
        var env = await NewEnv();
        var wh  = await WarehouseCode(env);

        var preview = await PreviewOk(env.Client, Sheet(R(wh, "P1", "5"), R("YOK", "P1", "1")));
        preview.GetProperty("errors").GetInt32().ShouldBe(1);
        var bad = preview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("action").GetString() == "error");
        bad.GetProperty("rowNumber").GetInt32().ShouldBe(3);
        bad.GetProperty("errors")[0].GetString()!.ShouldContain("YOK");

        using var commit = await Commit(env.Client, preview);
        await Fail(commit, HttpStatusCode.BadRequest, "import_has_errors");
        (await Movements(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task OpeningImport_UnknownProductAndBadQuantity_RowErrors() {
        var env = await NewEnv();
        var wh  = await WarehouseCode(env);

        var preview = await PreviewOk(env.Client, Sheet(R(wh, "YOK", "5"), R(wh, "P1", "-1"), R(wh, "P1", "abc")));
        preview.GetProperty("errors").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task OpeningImport_CommitIsAllOrNothing_WhenDataChangedAfterPreview() {
        var env   = await NewEnv();
        var wh    = await WarehouseCode(env);
        var other = await CreateWarehouse(env.Client, env.Branch, "W2");
        var other2 = await CreateProduct(env.Client, "P2");

        var preview = await PreviewOk(env.Client, Sheet(R(wh, "P1", "5"), R("W2", "P2", "1")));
        preview.GetProperty("errors").GetInt32().ShouldBe(0);

        // Önizlemeden sonra ikinci depo pasifleştirilir: uygulama bu satırda hata verir, ilk depoya da hiçbir şey yazılmaz.
        using var deactivate = await env.Client.PostAsync($"{Base}/warehouses/{other}/deactivate", null);
        deactivate.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var commit = await Commit(env.Client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.Conflict, await commit.Content.ReadAsStringAsync());
        (await commit.JsonAsync()).GetProperty("code").GetString().ShouldBe("import_conflict");
        (await Movements(env.Tenant)).ShouldBeEmpty();
        other2.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task OpeningImport_LotProduct_GroupsLotRows() {
        var env = await NewEnv(["seri-lot"]);
        var lot = await CreateProduct(env.Client, "LOT-1", new { tracksSerialLot = true, trackingMode = "lot" });
        var wh  = await WarehouseCode(env);

        var preview = await PreviewOk(env.Client, Sheet(R(wh, "LOT-1", "3", lot: "A", expiry: "2027-01-01"), R(wh, "LOT-1", "2", lot: "B")));
        preview.GetProperty("errors").GetInt32().ShouldBe(0);
        using var commit = await Commit(env.Client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        (await BalanceOf(env, env.Warehouse, lot)).GetProperty("physical").GetDecimal().ShouldBe(5m);
        (await Movements(env.Tenant)).Count.ShouldBe(1);
    }

    [Test]
    public async Task OpeningImport_Template_HasHeaders() {
        var env = await NewEnv();
        using var response = await env.Client.GetAsync($"{Base}/opening-import/template.xlsx");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        workbook.Worksheets.First().Cell(1, 1).GetString().ShouldBe("Depo kodu");
    }
}
