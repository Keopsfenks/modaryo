using System.Text.Json;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Inventory;

internal static class DocumentSupport {
    public static object DLine(Guid product, decimal quantity, decimal? cost = 10m, string unit = "adet", Guid? variant = null) {
        return new { productId = product, variantId = variant, unit, quantity, unitCost = cost };
    }

    public static object Document(string kind, Guid warehouse, string date, params object[] lines) {
        return new { kind, date, warehouseId = warehouse, reason = "Test", lines };
    }

    public static async Task<JsonElement> Created(HttpResponseMessage response) {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }
}

public class DocumentTests : TestBase {
    [Test]
    public async Task ReceiptDocument_CreatesMovementsAndNumber() {
        var env = await NewEnv();

        using var first  = await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-01", DocumentSupport.DLine(env.Product, 5m)));
        var       one    = await DocumentSupport.Created(first);
        using var second = await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-02", DocumentSupport.DLine(env.Product, 2m)));
        var       two    = await DocumentSupport.Created(second);

        one.GetProperty("number").GetString().ShouldBe("SF2026000001");
        two.GetProperty("number").GetString().ShouldBe("SF2026000002");
        var movements = await Movements(env.Tenant);
        movements.Count.ShouldBe(2);
        movements[0].Type.ToString().ShouldBe("Receipt");
        movements[0].SourceType.ShouldBe("stock-document");
        movements[0].SourceId.ShouldBe(one.GetProperty("id").GetGuid());
        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(7m);

        using var get = await env.Client.GetAsync($"{Base}/documents/{one.GetProperty("id").GetGuid()}");
        var       doc = await get.JsonAsync();
        doc.GetProperty("lines").GetArrayLength().ShouldBe(1);
        doc.GetProperty("status").GetString().ShouldBe("active");
    }

    [Test]
    public async Task OpeningAndIssueDocuments_UseMatchingMovementTypes() {
        var env = await NewEnv();
        await DocumentSupport.Created(await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("opening", env.Warehouse, "2026-01-01", DocumentSupport.DLine(env.Product, 10m))));
        await DocumentSupport.Created(await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("issue", env.Warehouse, "2026-02-01", DocumentSupport.DLine(env.Product, 4m, null))));

        var movements = await Movements(env.Tenant);
        movements.Select(m => m.Type.ToString()).ToArray().ShouldBe(["Opening", "Issue"]);
        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(6m);

        using var list = await env.Client.GetAsync($"{Base}/documents?kind=issue");
        (await list.JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task IssueMoreThanStock_Returns409() {
        var env = await NewEnv();
        using var response = await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("issue", env.Warehouse, "2026-02-01", DocumentSupport.DLine(env.Product, 1m, null)));
        await Fail(response, HttpStatusCode.Conflict, "insufficient_stock");

        using var list = await env.Client.GetAsync($"{Base}/documents");
        (await list.JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(0); // belge de yazılmadı
    }

    [Test]
    public async Task LineUnit_IsConvertedToBaseUnit() {
        var env  = await NewEnv();
        var item = await CreateProduct(env.Client, "KOLI", new { units = new[] { new { unit = "koli", factor = 10m, barcode = (string?)null } } });

        await DocumentSupport.Created(await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-01", DocumentSupport.DLine(item, 2m, 100m, "koli"))));

        var movement = (await Movements(env.Tenant)).Single();
        movement.Quantity.ShouldBe(20m);
        movement.UnitCost.ShouldBe(10m); // koli başına 100 -> adet başına 10

        using var bad = await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-01", DocumentSupport.DLine(item, 1m, 1m, "palet")));
        await Fail(bad, HttpStatusCode.BadRequest, "invalid_unit");
    }

    [Test]
    public async Task CancelDocument_Reverses() {
        var env = await NewEnv();
        var doc = await DocumentSupport.Created(await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-01", DocumentSupport.DLine(env.Product, 5m))));
        var id  = doc.GetProperty("id").GetGuid();

        using var cancel = await env.Client.PostAsJsonAsync($"{Base}/documents/{id}/cancel", new { reason = "Hatalı giriş" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        (await BalanceOf(env, env.Warehouse)).GetProperty("physical").GetDecimal().ShouldBe(0m);
        (await Movements(env.Tenant)).Count.ShouldBe(2);

        using var again = await env.Client.PostAsJsonAsync($"{Base}/documents/{id}/cancel", new { reason = "Tekrar" });
        await Fail(again, HttpStatusCode.Conflict, "document_cancelled");

        using var get = await env.Client.GetAsync($"{Base}/documents/{id}");
        (await get.JsonAsync()).GetProperty("status").GetString().ShouldBe("cancelled");
    }

    [Test]
    public async Task CancelDocument_RequiresReason() {
        var env = await NewEnv();
        var doc = await DocumentSupport.Created(await env.Client.PostAsJsonAsync($"{Base}/documents", DocumentSupport.Document("receipt", env.Warehouse, "2026-03-01", DocumentSupport.DLine(env.Product, 5m))));
        using var cancel = await env.Client.PostAsJsonAsync($"{Base}/documents/{doc.GetProperty("id").GetGuid()}/cancel", new { reason = "" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
