using System.Text.Json;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

public class CountTests : TestBase {
    private static async Task<Guid> StartCount(Env env, string name = "Yıl sonu") {
        using var response = await env.Client.PostAsJsonAsync($"{Base}/counts", new { warehouseId = env.Warehouse, categoryId = (Guid?)null, name });
        return (await DocumentSupport.Created(response)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SetCounted(Env env, Guid count, decimal? counted, Guid? product = null) {
        return env.Client.PutAsJsonAsync($"{Base}/counts/{count}/lines", new { lines = new[] { new { productId = product ?? env.Product, variantId = (Guid?)null, countedQty = counted } } });
    }

    [Test]
    public async Task CountPost_UsesSnapshotNotCurrent() {
        var env = await NewEnv();
        await Receive(env, 10m);
        var count = await StartCount(env);

        // Sayım başladıktan sonra 3 adet satıldı; sayılan miktar sayım anındaki rafı yansıtır (8).
        using var sale = await Issue(env, 3m, new DateOnly(2026, 3, 6));
        sale.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var set = await SetCounted(env, count, 8m);
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());

        using var post = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
        post.StatusCode.ShouldBe(HttpStatusCode.NoContent, await post.Content.ReadAsStringAsync());

        // 10 - 3 (satış) - 2 (sayım farkı = 8 - 10) = 5; "sayılan - mevcut" olsaydı 8 - 7 = +1 yazılırdı.
        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(5m);
        var adjustment = (await Movements(env.Tenant)).Single(m => m.Type.ToString() == "CountAdjustment");
        adjustment.Quantity.ShouldBe(-2m);
        adjustment.SourceType.ShouldBe("stock-count");

        using var get = await env.Client.GetAsync($"{Base}/counts/{count}");
        (await get.JsonAsync()).GetProperty("status").GetString().ShouldBe("posted");
    }

    [Test]
    public async Task CountPost_SurplusAndUncountedLines() {
        var env   = await NewEnv();
        var other = await CreateProduct(env.Client, "P2");
        await Receive(env, 10m);
        await Receive(env, 4m, 5m, product: other);
        var count = await StartCount(env);

        using var set = await SetCounted(env, count, 12m); // other sayılmadı
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent, await set.Content.ReadAsStringAsync());

        using var post = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
        post.StatusCode.ShouldBe(HttpStatusCode.NoContent, await post.Content.ReadAsStringAsync());

        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(12m);
        (await BalanceOf(env, env.Warehouse, other)).GetProperty("physical").GetDecimal().ShouldBe(4m);
    }

    [Test]
    public async Task CountTwiceOpen_Returns409() {
        var env = await NewEnv();
        await Receive(env, 10m);
        await StartCount(env);

        using var second = await env.Client.PostAsJsonAsync($"{Base}/counts", new { warehouseId = env.Warehouse, categoryId = (Guid?)null, name = "İkinci" });
        await Fail(second, HttpStatusCode.Conflict, "count_open_exists");
    }

    [Test]
    public async Task PostedCount_IsClosed() {
        var env   = await NewEnv();
        await Receive(env, 10m);
        var count = await StartCount(env);
        using var set = await SetCounted(env, count, 10m);
        set.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var post = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
        post.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var again = await env.Client.PostAsync($"{Base}/counts/{count}/post", null);
        await Fail(again, HttpStatusCode.Conflict, "count_closed");
        using var edit = await SetCounted(env, count, 1m);
        await Fail(edit, HttpStatusCode.Conflict, "count_closed");

        // yeni sayım artık açılabilir
        await StartCount(env, "Yeni");
    }

    [Test]
    public async Task Count_SnapshotListsItemsWithExpected() {
        var env   = await NewEnv();
        await Receive(env, 10m);
        var count = await StartCount(env);

        using var get  = await env.Client.GetAsync($"{Base}/counts/{count}");
        var       body = await get.JsonAsync();
        var       line = body.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("expectedQty").GetDecimal().ShouldBe(10m);
        line.GetProperty("countedQty").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("number").GetString()!.ShouldStartWith("SY");
    }
}
