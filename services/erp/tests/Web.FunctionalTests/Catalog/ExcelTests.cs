using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;

namespace ERP.Web.FunctionalTests.Catalog;

public class ExcelTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private const string Export   = "/api/erp/catalog/products/export.xlsx";
    private const string Template = "/api/erp/catalog/products/import-template.xlsx";
    private const string Preview  = "/api/erp/catalog/products/import/preview";

    private static readonly string[] Headers = [
        "Kod", "Ad", "Tür", "Kategori", "Marka", "Birim", "KDV", "Barkod", "Alış fiyatı", "Alış para birimi",
        "Alış KDV dahil", "Satış fiyatı", "Satış para birimi", "Satış KDV dahil", "Etiketler", "Pasif"
    ];

    private static byte[] Sheet(params Dictionary<string, string>[] rows) {
        using var workbook = new XLWorkbook();
        var       sheet    = workbook.AddWorksheet("Ürünler");
        for (var c = 0; c < Headers.Length; c++) sheet.Cell(1, c + 1).Value = Headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < Headers.Length; c++)
                if (rows[r].TryGetValue(Headers[c], out var value))
                    sheet.Cell(r + 2, c + 1).Value = value;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> Row(string code, string name, params (string Key, string Value)[] more) {
        var row = new Dictionary<string, string> { ["Kod"] = code, ["Ad"] = name };
        foreach (var (key, value) in more) row[key] = value;
        return row;
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] file, bool autoCode = false) {
        using var form    = new MultipartFormDataContent();
        var       content = new ByteArrayContent(file);
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "urunler.xlsx");
        if (autoCode) form.Add(new StringContent("true"), "autoCode");
        return await client.PostAsync(Preview, form);
    }

    private static async Task<JsonElement> PreviewOk(HttpClient client, byte[] file, bool autoCode = false) {
        using var response = await Upload(client, file, autoCode);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> Commit(HttpClient client, JsonElement preview) {
        return client.PostAsync($"/api/erp/catalog/products/import/{preview.GetProperty("batchId").GetGuid()}/commit",
                                null);
    }

    private static async Task<JsonElement> ListAll(HttpClient client) {
        using var response = await client.GetAsync($"{Products}?includeInactive=true&pageSize=100");
        return (await response.JsonAsync()).GetProperty("items");
    }

    private static ApplicationDbContext OpenDb() {
        var tenant  = new FixedTenant(Tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestEnvironment.ConnectionString).AddInterceptors(new TenantInterceptor(tenant)).Options;
        return new ApplicationDbContext(options, tenant);
    }

    [Test]
    public async Task Template_HasTurkishHeaders() {
        using var client   = await Manager(Tenant);
        using var response = await client.GetAsync(Template);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var       sheet    = workbook.Worksheet(1);
        Enumerable.Range(1, Headers.Length).Select(c => sheet.Cell(1, c).GetString()).ToArray().ShouldBe(Headers);
        sheet.Cell(2, 1).IsEmpty().ShouldBeTrue();
    }

    [Test]
    public async Task ExportThenImportSameFile_NoChanges() {
        using var client = await Manager(Tenant);
        var       brand  = await (await client.PostAsJsonAsync("/api/erp/catalog/brands", new { name = "Nike" })).JsonAsync();
        var       parent = await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Giyim" })).JsonAsync();
        var child = await (await client.PostAsJsonAsync("/api/erp/catalog/categories",
                                                        new { name = "Erkek", parentId = parent.GetProperty("id").GetGuid() })).JsonAsync();
        await CreateOk(client, new {
            kind = "product", code = "A1", name = "Tişört", categoryId = child.GetProperty("id").GetGuid(),
            brandId = brand.GetProperty("id").GetGuid(), vatRate = 10, barcode = "8690000000011",
            tags = new[] { "yaz", "pamuk" },
            defaultSalePrice = new { amount = 199.9m, currency = "TRY", vatIncluded = true },
            defaultPurchasePrice = new { amount = 80m, currency = "USD", vatIncluded = false }
        });
        await CreateOk(client, new { kind = "service", code = "H1", name = "Montaj", baseUnit = "saat" });
        var before = await ListAll(client);

        using var export = await client.GetAsync(Export);
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        var file = await export.Content.ReadAsByteArrayAsync();

        var preview = await PreviewOk(client, file);
        preview.GetProperty("total").GetInt32().ShouldBe(2);
        preview.GetProperty("creates").GetInt32().ShouldBe(0);
        preview.GetProperty("errors").GetInt32().ShouldBe(0);
        preview.GetProperty("updates").GetInt32().ShouldBe(2);

        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        (await ListAll(client)).GetRawText().ShouldBe(before.GetRawText());
        var card = await (await client.GetAsync($"{Products}/{before[0].GetProperty("id").GetGuid()}")).JsonAsync();
        card.GetProperty("barcode").GetString().ShouldBe("8690000000011");
        card.GetProperty("defaultPurchasePrice").GetProperty("currency").GetString().ShouldBe("USD");
        card.GetProperty("tags").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task Preview_ReportsCreatesUpdatesErrors() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new { kind = "product", code = "A1", name = "Eski ad" });

        var preview = await PreviewOk(client, Sheet(Row("A1", "Yeni ad"),
                                                    Row("A2", "Yeni kart"),
                                                    Row("A3", "")));

        preview.GetProperty("total").GetInt32().ShouldBe(3);
        preview.GetProperty("updates").GetInt32().ShouldBe(1);
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        preview.GetProperty("errors").GetInt32().ShouldBe(1);
        var rows = preview.GetProperty("rows");
        rows[0].GetProperty("action").GetString().ShouldBe("update");
        rows[0].GetProperty("rowNumber").GetInt32().ShouldBe(2);
        rows[1].GetProperty("action").GetString().ShouldBe("create");
        rows[2].GetProperty("action").GetString().ShouldBe("error");
        rows[2].GetProperty("rowNumber").GetInt32().ShouldBe(4);
        rows[2].GetProperty("errors").GetArrayLength().ShouldBeGreaterThan(0);

        // Önizleme hiçbir şey yazmaz.
        (await ListAll(client)).GetArrayLength().ShouldBe(1);
        (await ListAll(client))[0].GetProperty("name").GetString().ShouldBe("Eski ad");
    }

    [Test]
    public async Task Preview_InvalidVat_RowError() {
        using var client = await Manager(Tenant);

        var preview = await PreviewOk(client, Sheet(Row("A1", "Kart", ("KDV", "18")), Row("A2", "Kart 2", ("KDV", "abc"))));

        preview.GetProperty("errors").GetInt32().ShouldBe(2);
        preview.GetProperty("rows")[0].GetProperty("errors")[0].GetString()!.ShouldContain("KDV");

        // Hatalı önizleme uygulanamaz ve hiçbir şey yazılmaz.
        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await commit.JsonAsync()).GetProperty("code").GetString().ShouldBe("import_has_errors");
        (await ListAll(client)).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Commit_AppliesCreatesAndUpdates() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new { kind = "product", code = "A1", name = "Eski ad", vatRate = 20 });

        var preview = await PreviewOk(client, Sheet(Row("A1", "Yeni ad", ("KDV", "10"), ("Pasif", "Evet")),
                                                    Row("A2", "Kart", ("Satış fiyatı", "12,5"), ("Etiketler", "a, b"))));
        using var commit = await Commit(client, preview);

        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());
        var result = await commit.JsonAsync();
        result.GetProperty("created").GetInt32().ShouldBe(1);
        result.GetProperty("updated").GetInt32().ShouldBe(1);

        var items = (await ListAll(client)).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!);
        items["A1"].GetProperty("name").GetString().ShouldBe("Yeni ad");
        items["A1"].GetProperty("vatRate").GetInt32().ShouldBe(10);
        items["A1"].GetProperty("isActive").GetBoolean().ShouldBeFalse();
        items["A2"].GetProperty("defaultSalePrice").GetProperty("amount").GetDecimal().ShouldBe(12.5m);
        items["A2"].GetProperty("tags").GetArrayLength().ShouldBe(2);

        // Aynı önizleme ikinci kez uygulanamaz.
        using var again = await Commit(client, preview);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.JsonAsync()).GetProperty("code").GetString().ShouldBe("import_already_committed");
    }

    [Test]
    public async Task AutoCode_BlankCodeCreatesWithNextCode_WithoutOptionIsError() {
        using var client = await Manager(Tenant);
        var       file   = Sheet(Row("", "Otomatik"));

        var without = await PreviewOk(client, file);
        without.GetProperty("errors").GetInt32().ShouldBe(1);

        var preview = await PreviewOk(client, file, true);
        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        (await ListAll(client))[0].GetProperty("code").GetString().ShouldBe("U00001");
    }

    [Test]
    public async Task CommitAfterConflict_WritesNothing() {
        using var client = await Manager(Tenant);
        var preview = await PreviewOk(client, Sheet(Row("N1", "Birinci", ("Kategori", "Giyim > Erkek"), ("Marka", "Yeni Marka")),
                                                    Row("N2", "İkinci")));
        preview.GetProperty("creates").GetInt32().ShouldBe(2);

        // Önizleme ile uygulama arasında N2 kodu başka biri tarafından alınır.
        await CreateOk(client, new { kind = "product", code = "N2", name = "Araya giren" });

        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.Conflict, await commit.Content.ReadAsStringAsync());
        var body = await commit.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("import_conflict");
        var fresh = body.GetProperty("preview");
        fresh.GetProperty("batchId").GetGuid().ShouldNotBe(preview.GetProperty("batchId").GetGuid());
        fresh.GetProperty("rows")[0].GetProperty("action").GetString().ShouldBe("create");
        fresh.GetProperty("rows")[1].GetProperty("action").GetString().ShouldBe("update");

        // Hiçbir şey yazılmadı: N1 yok, kategori ve marka oluşmadı, N2 olduğu gibi.
        var items = await ListAll(client);
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("name").GetString().ShouldBe("Araya giren");
        (await (await client.GetAsync("/api/erp/catalog/categories")).JsonAsync()).GetArrayLength().ShouldBe(0);
        (await (await client.GetAsync("/api/erp/catalog/brands")).JsonAsync()).GetArrayLength().ShouldBe(0);

        // Taze önizleme de uygulanabilir (artık N2 güncellenir).
        using var retry = await Commit(client, fresh);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await ListAll(client)).GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task ConcurrentCommits_OnlyOneApplies() {
        using var client  = await Manager(Tenant);
        var       preview = await PreviewOk(client, Sheet(Row("", "Bir"), Row("", "İki"), Row("", "Üç")), true);
        preview.GetProperty("creates").GetInt32().ShouldBe(3);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Commit(client, preview)));
        try {
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            foreach (var other in responses.Where(r => r.StatusCode != HttpStatusCode.OK)) {
                other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await other.JsonAsync()).GetProperty("code").GetString().ShouldBe("import_already_committed");
            }
        } finally {
            foreach (var r in responses) r.Dispose();
        }

        var items = await ListAll(client);
        items.EnumerateArray().Select(i => i.GetProperty("code").GetString()).Order().ShouldBe(["U00001", "U00002", "U00003"]);
    }

    [Test]
    public async Task RequiredCustomField_DoesNotBlockImport() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new { kind = "product", code = "R1", name = "Eski" });
        (await client.PostAsJsonAsync("/api/erp/catalog/custom-fields", new {
            key = "garanti", label = "Garanti", type = "number", required = true, appliesToProduct = true,
            appliesToService = true
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var preview = await PreviewOk(client, Sheet(Row("R1", "Güncel"), Row("R2", "Yeni")));

        preview.GetProperty("errors").GetInt32().ShouldBe(0, preview.GetRawText());
        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());
        (await ListAll(client)).GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task Import1000Rows_AutoCode_FinishesInReasonableTime() {
        using var client = await Manager(Tenant);
        await CreateOk(client, new { kind = "product", code = "U00005", name = "Önceden var" });
        await CreateOk(client, new { kind = "product", code = "SABIT1", name = "Güncellenecek" });
        var rows = Enumerable.Range(1, 1000).Select(i => i == 1
                                                              ? Row("SABIT1", "Güncellendi")
                                                              : Row("", "Kart " + i, ("Kategori", "Giyim > Alt " + i % 7),
                                                                    ("Marka", "Marka " + i % 5),
                                                                    ("Barkod", "869" + i.ToString("D10")),
                                                                    ("Satış fiyatı", "12,3456"))).ToArray();
        var file = Sheet(rows);

        var watch     = System.Diagnostics.Stopwatch.StartNew();
        var preview   = await PreviewOk(client, file, true);
        var previewMs = watch.ElapsedMilliseconds;
        preview.GetProperty("errors").GetInt32().ShouldBe(0, preview.GetRawText()[..Math.Min(2000, preview.GetRawText().Length)]);
        using var commit  = await Commit(client, preview);
        var       totalMs = watch.ElapsedMilliseconds;
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());
        TestContext.Progress.WriteLine($"1000 satır içe aktarma: önizleme {previewMs} ms, uygulama {totalMs - previewMs} ms");

        totalMs.ShouldBeLessThan(30_000);
        var codes = new List<string>();
        for (var page = 1; page <= 11; page++) {
            using var response = await client.GetAsync($"{Products}?includeInactive=true&pageSize=100&page={page}");
            codes.AddRange((await response.JsonAsync()).GetProperty("items").EnumerateArray()
                               .Select(i => i.GetProperty("code").GetString()!));
        }

        codes.Count.ShouldBe(1001);
        codes.Distinct().Count().ShouldBe(1001);
        // U00005 elle verilmişti, sayaç 6'ya ilerlemişti; otomatik kodlar U00006'dan U01004'e kadar gider.
        codes.ShouldContain("U00006");
        codes.ShouldContain("U01004");
        (await (await client.GetAsync($"{Products}/next-code")).JsonAsync()).GetProperty("code").GetString().ShouldBe("U01005");
    }

    [Test]
    public async Task CommitExpired_Returns410() {
        using var client  = await Manager(Tenant);
        var       preview = await PreviewOk(client, Sheet(Row("E1", "Süresi dolacak")));

        await using (var db = OpenDb()) {
            var batch = await db.ImportBatches.SingleAsync(b => b.Id == preview.GetProperty("batchId").GetGuid());
            batch.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await commit.JsonAsync()).GetProperty("code").GetString().ShouldBe("import_expired");
        (await ListAll(client)).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Over5000Rows_Returns400() {
        using var client = await Manager(Tenant);
        var       rows   = Enumerable.Range(1, 5001).Select(i => Row("K" + i, "Kart " + i)).ToArray();

        using var response = await Upload(client, Sheet(rows));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("too_many_rows");
    }

    [Test]
    public async Task NotAnXlsx_Returns400() {
        using var client   = await Manager(Tenant);
        using var response = await Upload(client, "bu bir excel değil"u8.ToArray());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("invalid_file");
    }

    [Test]
    public async Task CategoryPath_CreatesMissingCategories() {
        using var client = await Manager(Tenant);
        var       preview = await PreviewOk(client, Sheet(Row("C1", "Birinci", ("Kategori", "Giyim > Erkek > Tişört")),
                                                          Row("C2", "İkinci", ("Kategori", "giyim > Erkek > Pantolon"))));
        preview.GetProperty("errors").GetInt32().ShouldBe(0);

        // Önizleme kategori oluşturmaz.
        (await (await client.GetAsync("/api/erp/catalog/categories")).JsonAsync()).GetArrayLength().ShouldBe(0);

        using var commit = await Commit(client, preview);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        var tree = await (await client.GetAsync("/api/erp/catalog/categories")).JsonAsync();
        tree.GetArrayLength().ShouldBe(1);
        var giyim = tree[0];
        giyim.GetProperty("name").GetString().ShouldBe("Giyim");
        var erkek = giyim.GetProperty("children")[0];
        erkek.GetProperty("name").GetString().ShouldBe("Erkek");
        erkek.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Order()
            .ShouldBe(["Pantolon", "Tişört"]);

        var items = (await ListAll(client)).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!);
        items["C1"].GetProperty("categoryPath").GetString().ShouldBe("Giyim > Erkek > Tişört");
        items["C2"].GetProperty("categoryPath").GetString().ShouldBe("Giyim > Erkek > Pantolon");
    }

    [Test]
    public async Task DuplicateCodeInFile_IsRowError() {
        using var client = await Manager(Tenant);

        var preview = await PreviewOk(client, Sheet(Row("D1", "Bir"), Row("D1", "İki")));

        preview.GetProperty("creates").GetInt32().ShouldBe(1);
        preview.GetProperty("errors").GetInt32().ShouldBe(1);
        preview.GetProperty("rows")[1].GetProperty("action").GetString().ShouldBe("error");
    }

    [Test]
    public async Task ReadOnly_CannotImport_ButCanExport() {
        using var read = await Factory.ClientAsync(Tenant, "katalog.read");

        (await read.GetAsync(Export)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await read.GetAsync(Template)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await Upload(read, Sheet(Row("X1", "Kart")));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed class FixedTenant(Guid tenantId) : ICurrentTenant {
        public Guid?                TenantId           => tenantId;
        public Guid?                UserId             => null;
        public string?              UserFullName       => "Test Kullanıcı";
        public IReadOnlyList<Guid>? BranchIds          => null;
        public decimal              MaxDiscountPercent => 0;

        public bool HasModule(string key) {
            return true;
        }

        public bool CanAccessBranch(Guid branchId) {
            return true;
        }
    }
}
