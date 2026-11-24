using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class BundleTests : TestBase {
    private static readonly Guid Tenant  = Guid.CreateVersion7();
    private static readonly Guid BranchA = Guid.CreateVersion7();

    private static Task<HttpResponseMessage> CreateRaw(HttpClient client, string code, object extra) {
        var body = new Dictionary<string, object?> { ["kind"] = "product", ["name"] = "Set " + code, ["code"] = code };
        foreach (var property in extra.GetType().GetProperties()) body[property.Name] = property.GetValue(extra);
        return client.PostAsJsonAsync(Products, body);
    }

    private static object Component(Guid productId, decimal quantity = 1m, Guid? variantId = null) {
        return new { productId, variantId, quantity };
    }

    [Test]
    public async Task Bundle_CreateWithComponents_RoundTrips() {
        using var client = await Client(Tenant);
        var       a      = await CreateProduct(client, "B-A");
        var       b      = await CreateProduct(client, "B-B");

        using var response = await CreateRaw(client, "SET1", new { isBundle = true, bundleComponents = new[] { Component(a, 2m), Component(b, 1.5m) } });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.JsonAsync();
        body.GetProperty("isBundle").GetBoolean().ShouldBeTrue();
        var components = body.GetProperty("bundleComponents").EnumerateArray().ToList();
        components.Count.ShouldBe(2);
        components.Single(c => c.GetProperty("productId").GetGuid() == a).GetProperty("quantity").GetDecimal().ShouldBe(2m);

        var loaded = await (await client.GetAsync($"{Products}/{body.GetProperty("id").GetGuid()}")).JsonAsync();
        loaded.GetProperty("bundleComponents").GetArrayLength().ShouldBe(2);
    }

    [Test]
    public async Task Bundle_NestedComponent_Returns409() {
        using var client = await Client(Tenant);
        var       a      = await CreateProduct(client, "N-A");
        var       inner  = await CreateProduct(client, "N-IN", new { isBundle = true, bundleComponents = new[] { Component(a) } });

        using var nested = await CreateRaw(client, "N-OUT", new { isBundle = true, bundleComponents = new[] { Component(inner) } });

        nested.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await nested.ErrorCodeAsync()).ShouldBe("bundle_nested");

        // Başka bir setin bileşeni olan ürün sonradan sete çevrilemez.
        using var convert = await client.PutAsJsonAsync($"{Products}/{a}", new {
            kind = "product", name = "Ürün N-A", code = "N-A", isBundle = true, bundleComponents = new[] { Component(inner) }
        });
        convert.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await convert.ErrorCodeAsync()).ShouldBe("bundle_nested");
    }

    [Test]
    public async Task Bundle_InvalidComponents_Return400() {
        using var client  = await Client(Tenant);
        var       a       = await CreateProduct(client, "V-A");
        var       service = (await CreateOk(client, new { kind = "service", name = "Montaj", code = "V-SRV" })).GetProperty("id").GetGuid();

        (await CreateRaw(client, "V-1", new { isBundle = true, bundleComponents = Array.Empty<object>() })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateRaw(client, "V-2", new { isBundle = true, bundleComponents = new[] { Component(a, 0m) } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateRaw(client, "V-3", new { isBundle = true, bundleComponents = new[] { Component(service) } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateRaw(client, "V-4", new { isBundle = true, bundleComponents = new[] { Component(Guid.NewGuid()) } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateRaw(client, "V-5", new { isBundle = true, bundleComponents = new[] { Component(a), Component(a) } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CreateRaw(client, "V-6", new { isBundle = false, bundleComponents = new[] { Component(a) } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TrackingMode_RequiresSerialLotFlag_AndDefaultsToLot() {
        using var client = await Client(Tenant, modules: ["seri-lot"]);

        using var ok = await CreateRaw(client, "T-1", new { tracksSerialLot = true, trackingMode = "serial" });
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        (await ok.JsonAsync()).GetProperty("trackingMode").GetString().ShouldBe("serial");

        using var defaulted = await CreateRaw(client, "T-2", new { tracksSerialLot = true });
        (await defaulted.JsonAsync()).GetProperty("trackingMode").GetString().ShouldBe("lot");

        using var withoutFlag = await CreateRaw(client, "T-3", new { trackingMode = "lot" });
        withoutFlag.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ChangeTrackingModeAfterMovements_Returns409() {
        SeedBranches(Tenant, Branch(BranchA));
        using var client    = await Client(Tenant, modules: ["seri-lot"]);
        var       warehouse = await DefaultWarehouse(client, BranchA);
        var       created   = await (await CreateRaw(client, "T-4", new { tracksSerialLot = true, trackingMode = "lot" })).JsonAsync();
        var       id        = created.GetProperty("id").GetGuid();

        object Body(string mode) {
            return new { kind = "product", name = "Set T-4", code = "T-4", tracksSerialLot = true, trackingMode = mode };
        }

        using (var before = await client.PutAsJsonAsync($"{Products}/{id}", Body("serial")))
            before.StatusCode.ShouldBe(HttpStatusCode.OK, await before.Content.ReadAsStringAsync());

        await AddMovementAsync(Tenant, warehouse, id);
        using var blocked = await client.PutAsJsonAsync($"{Products}/{id}", Body("lot"));

        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ErrorCodeAsync()).ShouldBe("tracking_mode_locked");
    }
}
