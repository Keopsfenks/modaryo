using System.Net.Http.Json;
using System.Text.Json;
using static ERP.Web.FunctionalTests.Pricing.PricingApi;

namespace ERP.Web.FunctionalTests.Pricing;

public class PriceListTests : TestBase {
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Test]
    public async Task CreateSaleList_AddItemsWithTiers_ReturnsRows() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1");
        var       list    = await CreateList(client, "Perakende");

        using var put = await PutRows(client, list, Row(product, Tiers((1, 100m), (10, 90m))));
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var items = await Items(client, list);
        items.Length.ShouldBe(1);
        items[0].GetProperty("productCode").GetString().ShouldBe("P1");
        items[0].GetProperty("derived").GetBoolean().ShouldBeFalse();
        items[0].GetProperty("tiers").GetArrayLength().ShouldBe(2);
        items[0].GetProperty("tiers")[1].GetProperty("price").GetDecimal().ShouldBe(90m);

        var lists = await (await client.GetAsync($"{Base}/lists")).JsonAsync();
        lists.EnumerateArray().Single().GetProperty("itemCount").GetInt32().ShouldBe(1);
    }

    [Test]
    public async Task InvalidTiers_Returns400InvalidTiers() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1");
        var       list    = await CreateList(client, "Perakende");

        foreach (var tiers in new[] { Tiers((2, 100m)), Tiers((1, 100m), (1, 90m)), Tiers((1, 100m), (5, 90m), (3, 80m)), Array.Empty<object>() }) {
            using var response = await PutRows(client, list, Row(product, tiers));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).ShouldBe("invalid_tiers");
        }

        (await Items(client, list)).ShouldBeEmpty();
    }

    [Test]
    public async Task DerivedList_ComputedWithRounding() {
        using var client = await Client(Tenant);
        await SetSettings(client, 0, new { rounding = "fiveCents" });
        var p1  = await CreateProduct(client, "P1");
        var p2  = await CreateProduct(client, "P2");
        var bas = await CreateList(client, "Baz");
        await PutRows(client, bas, Row(p1, Tiers((1, 10.03m), (10, 9m))), Row(p2, Tiers((1, 50m))));
        var derived = await CreateList(client, "Bayi", extra: new { baseListId = bas, adjustmentPercent = 10m });

        var items = await Items(client, derived);

        items.Length.ShouldBe(2);
        items.ShouldAllBe(i => i.GetProperty("derived").GetBoolean());
        var first = items.Single(i => i.GetProperty("productCode").GetString() == "P1");
        first.GetProperty("tiers")[0].GetProperty("price").GetDecimal().ShouldBe(11.05m); // 11,033 -> 11,05
        first.GetProperty("tiers")[1].GetProperty("price").GetDecimal().ShouldBe(9.90m);
        FirstPrice(items.Single(i => i.GetProperty("productCode").GetString() == "P2")).ShouldBe(55m);
    }

    [Test]
    public async Task DerivedList_ExplicitRowOverrides() {
        using var client = await Client(Tenant);
        var       p1     = await CreateProduct(client, "P1");
        var       p2     = await CreateProduct(client, "P2");
        var       bas    = await CreateList(client, "Baz");
        await PutRows(client, bas, Row(p1, Tiers((1, 100m))), Row(p2, Tiers((1, 50m))));
        var derived = await CreateList(client, "Bayi", extra: new { baseListId = bas, adjustmentPercent = -20m });
        await PutRows(client, derived, Row(p1, Tiers((1, 95m))));

        var items = await Items(client, derived);

        var explicitRow = items.Single(i => i.GetProperty("productCode").GetString() == "P1");
        explicitRow.GetProperty("derived").GetBoolean().ShouldBeFalse();
        FirstPrice(explicitRow).ShouldBe(95m);
        var computed = items.Single(i => i.GetProperty("productCode").GetString() == "P2");
        computed.GetProperty("derived").GetBoolean().ShouldBeTrue();
        FirstPrice(computed).ShouldBe(40m);
    }

    [Test]
    public async Task DerivedList_Cycle_Returns409() {
        using var client = await Client(Tenant);
        var       a      = await CreateList(client, "A");
        var       b      = await CreateList(client, "B", extra: new { baseListId = a, adjustmentPercent = 5m });

        using var cycle = await client.PutAsJsonAsync($"{Base}/lists/{a}", new {
            name = "A", currency = "TRY", vatIncluded = true, baseListId = b, adjustmentPercent = 5m
        });
        using var self = await client.PutAsJsonAsync($"{Base}/lists/{a}", new {
            name = "A", currency = "TRY", vatIncluded = true, baseListId = a, adjustmentPercent = 5m
        });

        cycle.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await cycle.ErrorCodeAsync()).ShouldBe("list_cycle");
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await self.ErrorCodeAsync()).ShouldBe("list_cycle");
    }

    [Test]
    public async Task DerivedList_TooDeep_Returns409() {
        using var client = await Client(Tenant);
        var       a      = await CreateList(client, "A");
        var       b      = await CreateList(client, "B", extra: new { baseListId = a, adjustmentPercent = 1m });
        var       c      = await CreateList(client, "C", extra: new { baseListId = b, adjustmentPercent = 1m });

        using var response = await client.PostAsJsonAsync($"{Base}/lists", new {
            name = "D", kind = "sale", currency = "TRY", vatIncluded = true, baseListId = c, adjustmentPercent = 1m
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("list_depth");
    }

    [Test]
    public async Task DerivedList_BaseMustMatchKindAndCurrency() {
        using var client   = await Client(Tenant);
        var       purchase = await CreateList(client, "Alış", "purchase");
        var       usd      = await CreateList(client, "USD", extra: new { currency = "USD" });

        using var kind = await client.PostAsJsonAsync($"{Base}/lists", new {
            name = "X", kind = "sale", currency = "TRY", vatIncluded = true, baseListId = purchase, adjustmentPercent = 1m
        });
        using var currency = await client.PostAsJsonAsync($"{Base}/lists", new {
            name = "Y", kind = "sale", currency = "TRY", vatIncluded = true, baseListId = usd, adjustmentPercent = 1m
        });

        kind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        currency.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task BulkAdjust_TenPercentByCategory_WritesHistory() {
        using var client = await Client(Tenant);
        var       giyim  = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Giyim" })).JsonAsync()).GetProperty("id").GetGuid();
        var       diger  = (await (await client.PostAsJsonAsync("/api/erp/catalog/categories", new { name = "Diğer" })).JsonAsync()).GetProperty("id").GetGuid();
        var       p1     = await CreateProduct(client, "P1", new { categoryId = giyim });
        var       p2     = await CreateProduct(client, "P2", new { categoryId = diger });
        var       list   = await CreateList(client, "Perakende");
        await PutRows(client, list, Row(p1, Tiers((1, 100m), (10, 80m))), Row(p2, Tiers((1, 50m))));

        using var response = await client.PostAsJsonAsync($"{Base}/lists/{list}/bulk-adjust", new { percent = 10m, categoryId = giyim });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.JsonAsync()).GetProperty("updated").GetInt32().ShouldBe(1);
        var items = await Items(client, list);
        var one   = items.Single(i => i.GetProperty("productCode").GetString() == "P1");
        one.GetProperty("tiers")[0].GetProperty("price").GetDecimal().ShouldBe(110m);
        one.GetProperty("tiers")[1].GetProperty("price").GetDecimal().ShouldBe(88m);
        FirstPrice(items.Single(i => i.GetProperty("productCode").GetString() == "P2")).ShouldBe(50m);
        var history = await History(client, $"?productId={p1}");
        history.Length.ShouldBe(2); // ilk kayıt + toplu değişiklik
        history[0].GetProperty("after")[0].GetProperty("price").GetDecimal().ShouldBe(110m);
        history[0].GetProperty("before")[0].GetProperty("price").GetDecimal().ShouldBe(100m);
        (await History(client, $"?productId={p2}")).Length.ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentRowUpdate_StaleGets409AndHistoryHasOneEntryPerSuccess() {
        using var client  = await Client(Tenant);
        var       product = await CreateProduct(client, "P1");
        var       list    = await CreateList(client, "Perakende");

        using var first = await PutRows(client, list, Row(product, Tiers((1, 100m))));
        var       v1    = (await first.JsonAsync()).GetProperty("rows")[0].GetProperty("version").GetUInt32();
        using var second = await PutRows(client, list, Row(product, Tiers((1, 110m)), v1));
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var       v2    = (await second.JsonAsync()).GetProperty("rows")[0].GetProperty("version").GetUInt32();
        using var stale = await PutRows(client, list, Row(product, Tiers((1, 120m)), v1));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ErrorCodeAsync()).ShouldBe("version_conflict");
        v2.ShouldNotBe(v1);
        FirstPrice((await Items(client, list))[0]).ShouldBe(110m);
        var history = await History(client, $"?productId={product}&scope=list");
        history.Length.ShouldBe(2);
        history[0].GetProperty("after")[0].GetProperty("price").GetDecimal().ShouldBe(110m);
        history[1].GetProperty("after")[0].GetProperty("price").GetDecimal().ShouldBe(100m);

        // Aynı sürümle eşzamanlı iki kayıt: yalnızca biri kazanır.
        var results = await Task.WhenAll(
            PutRows(client, list, Row(product, Tiers((1, 130m)), v2)),
            PutRows(client, list, Row(product, Tiers((1, 140m)), v2)));
        results.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await History(client, $"?productId={product}&scope=list")).Length.ShouldBe(3);
    }

    [Test]
    public async Task DeleteDefaultList_Returns409ListInUse() {
        using var client = await Client(Tenant);
        var       list   = await CreateList(client, "Varsayılan");
        await SetSettings(client, 0, new { defaultSaleListId = list });
        var bas = await CreateList(client, "Baz");
        await CreateList(client, "Türev", extra: new { baseListId = bas, adjustmentPercent = 5m });
        var       group   = await CreateList(client, "Grup listesi");
        using var created = await client.PostAsJsonAsync($"{Base}/customer-groups", new { name = "Bayiler", salePriceListId = group });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        foreach (var id in new[] { list, bas, group }) {
            using var response = await client.DeleteAsync($"{Base}/lists/{id}");
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.ErrorCodeAsync()).ShouldBe("list_in_use");
        }

        var free = await CreateList(client, "Boş");
        (await client.DeleteAsync($"{Base}/lists/{free}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task OtherTenant_Returns404() {
        using var owner   = await Client(Tenant);
        var       product = await CreateProduct(owner, "P1");
        var       list    = await CreateList(owner, "Perakende");
        await PutRows(owner, list, Row(product, Tiers((1, 100m))));
        var item = (await Items(owner, list))[0].GetProperty("id").GetGuid();

        using var other = await Client(Guid.CreateVersion7());

        (await other.GetAsync($"{Base}/lists/{list}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"{Base}/lists/{list}/items")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutRows(other, list, Row(product, Tiers((1, 1m))))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Base}/lists/{list}/items/{item}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Base}/lists/{list}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await other.GetAsync($"{Base}/lists")).JsonAsync()).GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task PurchaseList_WithoutAlisPermission_HiddenAndForbidden() {
        using var admin        = await Client(Tenant);
        var       sale         = await CreateList(admin, "Satış");
        var       sale2        = await CreateList(admin, "Satış 2");
        var       purchaseList = await CreateList(admin, "Tedarik", "purchase");

        using var reader = await Client(Tenant, "fiyat.read", "fiyat.manage", "katalog.read");

        var lists = (await (await reader.GetAsync($"{Base}/lists")).JsonAsync()).EnumerateArray().ToArray();
        lists.Select(l => l.GetProperty("id").GetGuid()).ShouldNotContain(purchaseList);
        lists.Length.ShouldBe(2);
        (await reader.GetAsync($"{Base}/lists/{purchaseList}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Base}/lists/{purchaseList}/items")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"{Base}/lists/{purchaseList}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"{Base}/lists/{sale}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        sale2.ShouldNotBe(Guid.Empty);
    }
}
