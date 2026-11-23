using System.Net.Http.Json;
using static ERP.Web.FunctionalTests.Catalog.CatalogTestSupport;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

public class CustomerPriceTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static object Body(Guid product, decimal price, string? from, string? to, string unit = "adet") {
        return new { productId = product, unit, currency = "TRY", vatIncluded = true, tiers = Tiers((1, price)), validFrom = from, validTo = to };
    }

    [Test]
    public async Task CustomerPrice_OverlappingDates_Returns409() {
        using var client   = await Client(Tenant);
        var       product  = await CreateProduct(client, "P1");
        var       customer = await CreateSupplier(client, "C1", false);
        var       url      = $"{Base}/customers/{customer}/prices";

        using var first = await client.PostAsJsonAsync(url, Body(product, 90m, "2026-01-01", "2026-06-30"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        using var overlap = await client.PostAsJsonAsync(url, Body(product, 80m, "2026-06-30", "2026-12-31"));
        using var open    = await client.PostAsJsonAsync(url, Body(product, 80m, null, null));
        using var after   = await client.PostAsJsonAsync(url, Body(product, 85m, "2026-07-01", "2026-12-31"));

        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overlap.ErrorCodeAsync()).ShouldBe("price_overlap");
        open.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        after.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Güncellerken kendisiyle çakışmaz; bir başkasına taşarsa çakışır.
        var id = (await first.JsonAsync()).GetProperty("id").GetGuid();
        (await client.PutAsJsonAsync($"{url}/{id}", Body(product, 91m, "2026-01-01", "2026-06-30"))).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        using var widen = await client.PutAsJsonAsync($"{url}/{id}", Body(product, 91m, "2026-01-01", "2026-07-15"));
        widen.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var list = (await (await client.GetAsync(url)).JsonAsync()).EnumerateArray().ToArray();
        list.Length.ShouldBe(2);
        (await History(client, $"?productId={product}&scope=customer")).Length.ShouldBe(3);
    }

    [Test]
    public async Task CustomerPrice_InvalidTiersAndUnit_Returns400() {
        using var client   = await Client(Tenant);
        var       product  = await CreateProduct(client, "P1");
        var       customer = await CreateSupplier(client, "C1", false);
        var       url      = $"{Base}/customers/{customer}/prices";

        using var tiers = await client.PostAsJsonAsync(url, new {
            productId = product, unit = "adet", currency = "TRY", vatIncluded = true, tiers = Tiers((5, 10m))
        });
        using var unit = await client.PostAsJsonAsync(url, Body(product, 10m, null, null, "koli"));

        tiers.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tiers.ErrorCodeAsync()).ShouldBe("invalid_tiers");
        unit.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CustomerGroup_Crud() {
        using var client = await Client(Tenant);
        var       list   = await CreateList(client, "Bayi");
        using var create = await client.PostAsJsonAsync($"{Base}/customer-groups", new { name = "Bayiler", salePriceListId = list });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await create.JsonAsync()).GetProperty("id").GetGuid();

        using var dup = await client.PostAsJsonAsync($"{Base}/customer-groups", new { name = "Bayiler" });
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var update = await client.PutAsJsonAsync($"{Base}/customer-groups/{id}", new { name = "Toptancılar", salePriceListId = (Guid?)null });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var groups = (await (await client.GetAsync($"{Base}/customer-groups")).JsonAsync()).EnumerateArray().ToArray();
        groups.Single().GetProperty("name").GetString().ShouldBe("Toptancılar");

        (await client.DeleteAsync($"{Base}/customer-groups/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"{Base}/customer-groups/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
