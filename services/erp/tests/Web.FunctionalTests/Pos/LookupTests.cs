using ERP.Domain.Catalog;
using ERP.Web.FunctionalTests.Inventory;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class LookupTests : TestBase {
    [Test]
    public async Task Lookup_ScaleBarcodeResolvesWeight() {
        var env = await NewEnv();
        await SetScaleFormats(env.Client);
        await Product(env.Client, "12345", new { baseUnit = "kg" }, price: 80m);

        // 27 | 12345 | 01234 g = 1,234 kg
        var hit = await Lookup(env.Client, Ean13.Build("2712345", 1234));
        hit.GetProperty("quantity").GetDecimal().ShouldBe(1.234m);
        hit.GetProperty("unit").GetString().ShouldBe("kg");
        hit.GetProperty("unitPrice").GetDecimal().ShouldBe(80m);
        hit.GetProperty("fixedTotal").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        hit.GetProperty("item").GetProperty("code").GetString().ShouldBe("12345");

        // Kontrol hanesi hatalı terazi barkodu.
        var good = Ean13.Build("2712345", 1234);
        var bad  = good[..12] + ((good[12] - '0' + 1) % 10);
        using var response = await env.Client.GetAsync($"{Api}/lookup?code={bad}");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.ErrorCodeAsync()).ShouldBe("scale_barcode_invalid");
    }

    [Test]
    public async Task Lookup_ScalePriceKind_QuantityFromEmbeddedPrice() {
        var env = await NewEnv();
        await SetScaleFormats(env.Client);
        await Product(env.Client, "12345", new { baseUnit = "kg" }, price: 80m);   // KDV hariç 80 => KDV dahil 96

        // 28 | 12345 | 04800 kuruş = 48,00 TL => 48 / 96 = 0,5 kg
        var hit = await Lookup(env.Client, Ean13.Build("2812345", 4800));
        hit.GetProperty("quantity").GetDecimal().ShouldBe(0.5m);
        hit.GetProperty("fixedTotal").GetDecimal().ShouldBe(48m);

        // Bölünemeyen tutar: 40 / 96 = 0,41667 => 0,417; satır toplamı barkoddaki tutardır.
        var uneven = await Lookup(env.Client, Ean13.Build("2812345", 4000));
        uneven.GetProperty("quantity").GetDecimal().ShouldBe(0.417m);
        uneven.GetProperty("fixedTotal").GetDecimal().ShouldBe(40m);
    }

    [Test]
    public async Task Lookup_ScaleItemUnknown_Returns404() {
        var env = await NewEnv();
        await SetScaleFormats(env.Client);

        await Lookup(env.Client, Ean13.Build("2799999", 100), HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Lookup_UnitBarcodeKoli() {
        var env = await NewEnv();
        await Product(env.Client, "SU", new { barcode = "8690000000011", units = new[] { new { unit = "koli", factor = 12m, barcode = "8690000000028" } } }, price: 10m);

        var koli = await Lookup(env.Client, "8690000000028");
        koli.GetProperty("unit").GetString().ShouldBe("koli");
        koli.GetProperty("factor").GetDecimal().ShouldBe(12m);
        koli.GetProperty("quantity").GetDecimal().ShouldBe(1m);
        koli.GetProperty("unitPrice").GetDecimal().ShouldBe(120m);

        var single = await Lookup(env.Client, "8690000000011");
        single.GetProperty("unit").GetString().ShouldBe("adet");
        single.GetProperty("unitPrice").GetDecimal().ShouldBe(10m);
    }

    [Test]
    public async Task Lookup_ResolvesProductCodeAndVariantCode_ElseNotFound() {
        var env = await NewEnv();
        await Product(env.Client, "EKM-1", price: 25m);
        await Product(env.Client, "TSR", new {
            attributes = new[] { new { name = "Beden", values = new[] { "S" } } },
            variants   = new[] { new { code = "TSR-S", values = new Dictionary<string, string> { ["Beden"] = "S" } } }
        }, price: 50m);

        (await Lookup(env.Client, "ekm-1")).GetProperty("item").GetProperty("code").GetString().ShouldBe("EKM-1");
        var variant = await Lookup(env.Client, "TSR-S");
        variant.GetProperty("item").GetProperty("variantId").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
        await Lookup(env.Client, "TSR", HttpStatusCode.NotFound);   // varyantlı kartın kendisi satılamaz
        await Lookup(env.Client, "YOK-1", HttpStatusCode.NotFound);

        using var search = await env.Client.GetAsync($"{Api}/lookup/search?q=ekm");
        search.StatusCode.ShouldBe(HttpStatusCode.OK, await search.Content.ReadAsStringAsync());
        (await search.JsonAsync()).GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Lookup_ShowsOtherStoresAvailable() {
        var env     = await NewEnv();
        var product = await Product(env.Client, "EKM", price: 10m);
        await InventoryTestSupport.AddMovementAsync(env.Tenant, env.Warehouse, product, null, 3m);
        await InventoryTestSupport.AddMovementAsync(env.Tenant, env.OtherWarehouse, product, null, 7m);
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();

        var hit    = await Lookup(env.Client, "EKM", registerId: register);
        var stores = hit.GetProperty("otherStores");
        stores.GetArrayLength().ShouldBe(1);
        stores[0].GetProperty("branchName").GetString().ShouldBe("Kadıköy");
        stores[0].GetProperty("available").GetDecimal().ShouldBe(7m);

        // Kasa verilmezse tüm şubelerin stoğu listelenir; şube erişimi gerekmez (yalnızca miktar).
        var restricted = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, [env.Branch], null, ["pos.sell"]);
        var hitRestricted = await Lookup(restricted, "EKM", registerId: register);
        hitRestricted.GetProperty("otherStores")[0].GetProperty("available").GetDecimal().ShouldBe(7m);
    }

    [Test]
    public async Task Lookup_RequiresSellPermission() {
        var env     = await NewEnv();
        await Product(env.Client, "EKM");
        var noSell = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["pos.shift"]);

        await Lookup(noSell, "EKM", HttpStatusCode.Forbidden);
    }
}
