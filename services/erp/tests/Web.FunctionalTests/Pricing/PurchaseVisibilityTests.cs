using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

/// <summary>fiyat.alis izni olmayan kullanıcıya alış fiyatı hiçbir yoldan sızmamalıdır.</summary>
public class PurchaseVisibilityTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    // Sızıntı aramasında ham gövdede aranan, başka hiçbir yerde geçmeyen tutarlar.
    private const decimal ProductPurchase = 123.45m;
    private const decimal VariantPurchase = 67.89m;
    private const string  Products        = "/api/erp/catalog/products";

    private static object Money(decimal amount) {
        return new { amount, currency = "TRY", vatIncluded = true };
    }

    private static Task<HttpClient> Limited() {
        return Client(Tenant, "katalog.read", "katalog.manage", "fiyat.read", "fiyat.manage");
    }

    private sealed record Seed(Guid ProductId, Guid VariantId, Guid PlainId);

    /// <summary>Alış fiyatlı varyantlı kart ve alış fiyatlı düz kart. Kodlar prefix-V, prefix-V-S ve prefix-P; barkodlar prefix8690001/2.</summary>
    private static async Task<Seed> SeedAsync(HttpClient admin, string prefix) {
        var created = await CreateOk(admin, new {
            kind = "product", name = "Gömlek", code = prefix + "-V", defaultSalePrice = Money(200m),
            defaultPurchasePrice = Money(ProductPurchase),
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } }
        });
        var productId = created.GetProperty("id").GetGuid();
        var variant   = created.GetProperty("variants")[0];
        var variantId = variant.GetProperty("id").GetGuid();
        using var put = await admin.PutAsJsonAsync($"{Products}/{productId}/variants/{variantId}", new {
            code = variant.GetProperty("code").GetString(), barcode = prefix + "8690001",
            salePriceOverride = Money(210m), purchasePriceOverride = Money(VariantPurchase)
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var plain = await CreateOk(admin, new {
            kind = "product", name = "Kemer", code = prefix + "-P", barcode = prefix + "8690002",
            defaultSalePrice = Money(50m), defaultPurchasePrice = Money(ProductPurchase)
        });
        return new Seed(productId, variantId, plain.GetProperty("id").GetGuid());
    }

    private static async Task<string> Body(HttpResponseMessage response) {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        response.Dispose();
        return text;
    }

    private static JsonElement Json(string body) {
        return JsonDocument.Parse(body).RootElement;
    }

    private static void NoLeak(string body) {
        body.ShouldNotContain(ProductPurchase.ToString(CultureInfo.InvariantCulture));
        body.ShouldNotContain(VariantPurchase.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task NoAlisPermission_ProductDtoPurchasePriceNull() {
        using var admin = await Client(Tenant);
        var       seed  = await SeedAsync(admin, "A");
        using var user  = await Limited();

        var body = await Body(await user.GetAsync($"{Products}/{seed.ProductId}"));
        NoLeak(body);
        var dto = Json(body);
        dto.GetProperty("defaultPurchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        dto.GetProperty("variants")[0].GetProperty("purchasePriceOverride").ValueKind.ShouldBe(JsonValueKind.Null);
        // Satış fiyatları etkilenmez.
        dto.GetProperty("defaultSalePrice").GetProperty("amount").GetDecimal().ShouldBe(200m);
        dto.GetProperty("variants")[0].GetProperty("salePriceOverride").GetProperty("amount").GetDecimal().ShouldBe(210m);

        NoLeak(await Body(await user.GetAsync($"{Products}?includeInactive=true&pageSize=100")));
        NoLeak(await Body(await user.GetAsync("/api/erp/catalog/options")));
    }

    [Test]
    public async Task NoAlisPermission_WritesNeverLeakAndNeverWipePurchase() {
        using var admin = await Client(Tenant);
        var       seed  = await SeedAsync(admin, "W");
        using var user  = await Limited();

        // Formu olduğu gibi (alış alanı null) geri gönderen kullanıcı kayıtlı alış fiyatını silemez.
        using var update = await user.PutAsJsonAsync($"{Products}/{seed.PlainId}", new {
            kind = "product", name = "Kemer yeni", code = "W-P", defaultSalePrice = Money(55m), defaultPurchasePrice = (object?)null
        });
        NoLeak(await Body(update));
        using var variant = await user.PutAsJsonAsync($"{Products}/{seed.ProductId}/variants/{seed.VariantId}", new {
            code = "W-V-S", salePriceOverride = Money(215m), purchasePriceOverride = Money(1m)
        });
        NoLeak(await Body(variant));
        // Yetkisiz kullanıcının gönderdiği alış fiyatı da yazılmaz.
        using var attempt = await user.PutAsJsonAsync($"{Products}/{seed.PlainId}", new {
            kind = "product", name = "Kemer yeni", code = "W-P", defaultSalePrice = Money(55m), defaultPurchasePrice = Money(1m)
        });
        NoLeak(await Body(attempt));

        var plain = Json(await Body(await admin.GetAsync($"{Products}/{seed.PlainId}")));
        plain.GetProperty("defaultSalePrice").GetProperty("amount").GetDecimal().ShouldBe(55m);
        plain.GetProperty("defaultPurchasePrice").GetProperty("amount").GetDecimal().ShouldBe(ProductPurchase);
        var card = Json(await Body(await admin.GetAsync($"{Products}/{seed.ProductId}")));
        card.GetProperty("variants")[0].GetProperty("salePriceOverride").GetProperty("amount").GetDecimal().ShouldBe(215m);
        card.GetProperty("variants")[0].GetProperty("purchasePriceOverride").GetProperty("amount").GetDecimal().ShouldBe(VariantPurchase);

        // Yetkisiz kullanıcının oluşturduğu kartta alış fiyatı kaydedilmez.
        using var create = await user.PostAsJsonAsync(Products, new {
            kind = "product", name = "Yeni", code = "W-N", defaultPurchasePrice = Money(9m)
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var createdId = (await create.JsonAsync()).GetProperty("id").GetGuid();
        Json(await Body(await admin.GetAsync($"{Products}/{createdId}"))).GetProperty("defaultPurchasePrice").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task NoAlisPermission_ItemLookupPurchasePriceNull() {
        using var admin = await Client(Tenant);
        await SeedAsync(admin, "I");
        using var user = await Limited();

        var search = await Body(await user.GetAsync("/api/erp/catalog/items?search=I-"));
        NoLeak(search);
        var items = Json(search).EnumerateArray().ToArray();
        items.Length.ShouldBe(2);
        items.ShouldAllBe(i => i.GetProperty("purchasePrice").ValueKind == JsonValueKind.Null);
        items.ShouldAllBe(i => i.GetProperty("salePrice").ValueKind == JsonValueKind.Object);

        foreach (var barcode in new[] { "I8690001", "I8690002" }) {
            var hit = await Body(await user.GetAsync($"/api/erp/catalog/items/barcode/{barcode}"));
            NoLeak(hit);
            Json(hit).GetProperty("item").GetProperty("purchasePrice").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task NoAlisPermission_ListsExcludePurchaseLists() {
        using var admin    = await Client(Tenant);
        var       sale     = await CreateList(admin, "Satış L");
        var       purchase = await CreateList(admin, "Alış L", "purchase");
        var       product  = await CreateProduct(admin, "L1");
        (await PutRows(admin, purchase, Row(product, Tiers((1, 77.77m))))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var user = await Limited();

        var body = await Body(await user.GetAsync($"{Base}/lists?includeInactive=true"));
        var ids  = Json(body).EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();
        ids.ShouldContain(sale);
        ids.ShouldNotContain(purchase);
        body.ShouldNotContain("Alış L");
        (await Body(await user.GetAsync($"{Base}/lists?kind=purchase"))).ShouldNotContain("Alış L");
        (await user.GetAsync($"{Base}/lists/{purchase}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync($"{Base}/lists/{purchase}/items")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.GetAsync($"{Base}/quote?kind=purchase&productId={product}&unit=adet")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task NoAlisPermission_HistoryExcludesPurchaseEntries() {
        using var admin = await Client(Tenant);
        var       seed  = await SeedAsync(admin, "H");
        (await admin.PutAsJsonAsync($"{Products}/{seed.PlainId}", new {
            kind = "product", name = "Kemer", code = "H-P", defaultSalePrice = Money(50m), defaultPurchasePrice = Money(88.88m)
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await CreateList(admin, "Alış H", "purchase");
        (await PutRows(admin, list, Row(seed.PlainId, Tiers((1, 66.66m))))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var user = await Limited();

        foreach (var query in new[] { "", $"?productId={seed.PlainId}", $"?productId={seed.PlainId}&scope=card", $"?productId={seed.PlainId}&scope=list" }) {
            var body = await Body(await user.GetAsync($"{Base}/history{query}"));
            body.ShouldNotContain("88.88");
            body.ShouldNotContain("66.66");
            NoLeak(body);
            Json(body).GetProperty("items").EnumerateArray().ShouldAllBe(h => h.GetProperty("kind").GetString() == "sale");
        }

        (await History(admin, $"?productId={seed.PlainId}")).Any(h => h.GetProperty("kind").GetString() == "purchase").ShouldBeTrue();
    }

    [Test]
    public async Task NoAlisPermission_ExportOmitsPurchaseColumns() {
        using var admin = await Client(Tenant);
        await SeedAsync(admin, "E");
        using var user = await Limited();

        foreach (var url in new[] { $"{Products}/export.xlsx", $"{Products}/import-template.xlsx" }) {
            using var response = await user.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
            var       sheet    = workbook.Worksheet(1);
            var       headers  = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            headers.ShouldNotContain(h => h.StartsWith("Alış"));
            headers.ShouldContain("Satış fiyatı");
            var cells = sheet.CellsUsed().Select(c => c.GetString()).ToList();
            cells.ShouldNotContain(c => c.Contains("123") && c.Contains("45"));
            cells.ShouldNotContain(c => c.Contains("67") && c.Contains("89"));
        }

        using var adminExport = await admin.GetAsync($"{Products}/export.xlsx");
        using var adminBook   = new XLWorkbook(await adminExport.Content.ReadAsStreamAsync());
        adminBook.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ShouldContain("Alış fiyatı");
    }

    private static byte[] Sheet(string[] headers, params string[][] rows) {
        using var workbook = new XLWorkbook();
        var       sheet    = workbook.AddWorksheet("Ürünler");
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<JsonElement> Preview(HttpClient client, byte[] file) {
        using var form    = new MultipartFormDataContent();
        var       content = new ByteArrayContent(file);
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "urunler.xlsx");
        using var response = await client.PostAsync($"{Products}/import/preview", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    [Test]
    public async Task NoAlisPermission_ImportWithPurchaseColumn_RowError() {
        using var admin = await Client(Tenant);
        var       seed  = await SeedAsync(admin, "M");
        using var user  = await Limited();

        var preview = await Preview(user, Sheet(["Kod", "Ad", "Alış fiyatı", "Satış fiyatı"],
                                                ["M-P", "Kemer", "5", "60"],
                                                ["M-OK", "Yeni ürün", "", "10"]));

        var rows = preview.GetProperty("rows").EnumerateArray().ToArray();
        rows[0].GetProperty("action").GetString().ShouldBe("error");
        rows[0].GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.Contains("Alış fiyatı için yetkiniz yok"));
        rows[1].GetProperty("action").GetString().ShouldBe("create");

        // Alış sütunsuz dosya (yetkisiz dışa aktarmanın biçimi) alış fiyatını silmeden güncellenir.
        var clean = await Preview(user, Sheet(["Kod", "Ad", "Satış fiyatı"], ["M-P", "Kemer", "61"]));
        clean.GetProperty("rows")[0].GetProperty("action").GetString().ShouldBe("update");
        using var commit = await user.PostAsync($"{Products}/import/{clean.GetProperty("batchId").GetGuid()}/commit", null);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK, await commit.Content.ReadAsStringAsync());

        var plain = Json(await Body(await admin.GetAsync($"{Products}/{seed.PlainId}")));
        plain.GetProperty("defaultSalePrice").GetProperty("amount").GetDecimal().ShouldBe(61m);
        plain.GetProperty("defaultPurchasePrice").GetProperty("amount").GetDecimal().ShouldBe(ProductPurchase);
    }

    [Test]
    public async Task WithAlis_AllVisible() {
        using var admin = await Client(Tenant);
        var       seed  = await SeedAsync(admin, "Z");
        var       list  = await CreateList(admin, "Alış Z", "purchase");
        (await PutRows(admin, list, Row(seed.PlainId, Tiers((1, 44.44m))))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var dto = Json(await Body(await admin.GetAsync($"{Products}/{seed.ProductId}")));
        dto.GetProperty("defaultPurchasePrice").GetProperty("amount").GetDecimal().ShouldBe(ProductPurchase);
        dto.GetProperty("variants")[0].GetProperty("purchasePriceOverride").GetProperty("amount").GetDecimal().ShouldBe(VariantPurchase);

        var items = Json(await Body(await admin.GetAsync("/api/erp/catalog/items?search=Z-"))).EnumerateArray().ToArray();
        items.Select(i => i.GetProperty("purchasePrice").GetProperty("amount").GetDecimal()).OrderBy(x => x)
            .ShouldBe([VariantPurchase, ProductPurchase]);
        Json(await Body(await admin.GetAsync("/api/erp/catalog/items/barcode/Z8690002"))).GetProperty("item")
            .GetProperty("purchasePrice").GetProperty("amount").GetDecimal().ShouldBe(ProductPurchase);

        Json(await Body(await admin.GetAsync($"{Base}/lists"))).EnumerateArray()
            .Select(l => l.GetProperty("id").GetGuid()).ShouldContain(list);
        (await History(admin, $"?productId={seed.PlainId}")).Any(h => h.GetProperty("kind").GetString() == "purchase").ShouldBeTrue();

        using var export = await admin.GetAsync($"{Products}/export.xlsx");
        using var book   = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        book.Worksheet(1).CellsUsed().Select(c => c.GetString()).ShouldContain(c => c.Contains("Alış"));
    }
}
