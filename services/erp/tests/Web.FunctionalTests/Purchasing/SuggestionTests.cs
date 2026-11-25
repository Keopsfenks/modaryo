using System.Text.Json;
using ERP.Web.FunctionalTests.Inventory;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class SuggestionTests : TestBase {
    private static async Task SetMinimum(Env env, Guid warehouse, Guid product, decimal min, decimal? reorder = null) {
        using var put = await env.Client.PutAsJsonAsync($"{InventoryTestSupport.Base}/minimums", new {
            warehouseId = warehouse, rows = new[] { new { productId = product, variantId = (Guid?)null, minQty = min, reorderQty = reorder } }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> ProductWithSupplier(Env env, string code, Guid? supplier) {
        return await InventoryTestSupport.CreateProduct(env.Client, code, supplier is null
            ? new { vatRate = 20, baseUnit = "adet" }
            : new { vatRate = 20, baseUnit = "adet", suppliers = new[] { new { currentAccountId = supplier.Value, supplierCode = code, isDefault = true } } });
    }

    private static async Task<JsonElement> Suggestions(Env env, HttpClient? client = null) {
        using var response = await (client ?? env.Client).GetAsync($"{Api}/suggestions?branchId={env.Branch}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("groups");
    }

    [Test]
    public async Task Suggestions_GroupedByDefaultSupplier() {
        var env       = await NewPurchasingEnv();
        var supplier2 = await CreateCari(env.Client, "T2", customer: false, supplier: true, name: "Tedarikçi İki");
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var a         = await ProductWithSupplier(env, "A1", env.Supplier);
        var b         = await ProductWithSupplier(env, "B1", supplier2);
        await SetMinimum(env, warehouse, a, 10m, 25m);
        await InventoryTestSupport.AddMovementAsync(env.Tenant, warehouse, a, quantity: 4m);
        using var put = await env.Client.PutAsJsonAsync($"{InventoryTestSupport.Base}/minimums", new {
            warehouseId = warehouse, rows = new[] {
                new { productId = a, variantId = (Guid?)null, minQty = 10m, reorderQty = (decimal?)25m },
                new { productId = b, variantId = (Guid?)null, minQty = 8m, reorderQty = (decimal?)null }
            }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var groups = await Suggestions(env);

        groups.GetArrayLength().ShouldBe(2);
        var first = groups.EnumerateArray().Single(g => g.GetProperty("supplierId").GetGuid() == env.Supplier);
        var line  = first.GetProperty("lines")[0];
        line.GetProperty("productId").GetGuid().ShouldBe(a);
        line.GetProperty("available").GetDecimal().ShouldBe(4m);
        line.GetProperty("suggestedQty").GetDecimal().ShouldBe(25m); // max(sipariş miktarı 25, 10 - 4)
        var second = groups.EnumerateArray().Single(g => g.GetProperty("supplierId").GetGuid() == supplier2);
        second.GetProperty("supplierName").GetString().ShouldBe("Tedarikçi İki");
        second.GetProperty("lines")[0].GetProperty("suggestedQty").GetDecimal().ShouldBe(8m); // max(0, 8 - 0)
    }

    [Test]
    public async Task Suggestions_ReducedByOpenPoQuantity() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var product   = await ProductWithSupplier(env, "A1", env.Supplier);
        await SetMinimum(env, warehouse, product, 10m);

        var draft = await CreatePo(env, PoBody(env, [PoLine(product, 4m)]));
        (await Suggestions(env))[0].GetProperty("lines")[0].GetProperty("suggestedQty").GetDecimal().ShouldBe(10m); // taslak yolda sayılmaz

        await Approve(env.Client, draft.GetProperty("id").GetGuid());
        var line = (await Suggestions(env))[0].GetProperty("lines")[0];
        line.GetProperty("onOrder").GetDecimal().ShouldBe(4m);
        line.GetProperty("suggestedQty").GetDecimal().ShouldBe(6m);

        var covering = await CreatePo(env, PoBody(env, [PoLine(product, 6m)]));
        await Approve(env.Client, covering.GetProperty("id").GetGuid());
        (await Suggestions(env)).GetArrayLength().ShouldBe(0); // yolda olan miktar ihtiyacı karşılıyor
    }

    [Test]
    public async Task CreateOrders_OneDraftPerSupplier() {
        var env       = await NewPurchasingEnv();
        var supplier2 = await CreateCari(env.Client, "T2", customer: false, supplier: true);
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var a1        = await ProductWithSupplier(env, "A1", env.Supplier);
        var a2        = await ProductWithSupplier(env, "A2", env.Supplier);
        var b         = await ProductWithSupplier(env, "B1", supplier2);

        var body = new {
            groups = new object[] {
                new { supplierId = env.Supplier, lines = new[] { new { productId = a1, variantId = (Guid?)null, warehouseId = warehouse, quantity = 5m },
                                                                  new { productId = a2, variantId = (Guid?)null, warehouseId = warehouse, quantity = 7m } } },
                new { supplierId = supplier2, lines = new[] { new { productId = b, variantId = (Guid?)null, warehouseId = warehouse, quantity = 3m } } }
            }
        };
        var orders = await Post(env.Client, "suggestions/create-orders", body, HttpStatusCode.Created);

        orders.GetArrayLength().ShouldBe(2);
        orders.EnumerateArray().ShouldAllBe(o => o.GetProperty("status").GetString() == "draft");
        var forFirst = orders.EnumerateArray().Single(o => o.GetProperty("supplierId").GetGuid() == env.Supplier);
        forFirst.GetProperty("lines").GetArrayLength().ShouldBe(2);
        forFirst.GetProperty("number").GetString()!.ShouldMatch(@"^SA2026\d{6}$");
        orders.EnumerateArray().Select(o => o.GetProperty("number").GetString()).Distinct().Count().ShouldBe(2);
    }

    [Test]
    public async Task ItemsWithoutSupplier_InTedarikcisizGroup() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var product   = await ProductWithSupplier(env, "N1", null);
        await SetMinimum(env, warehouse, product, 5m);

        var groups = await Suggestions(env);

        groups.GetArrayLength().ShouldBe(1);
        groups[0].GetProperty("supplierId").ValueKind.ShouldBe(JsonValueKind.Null);
        groups[0].GetProperty("supplierName").GetString().ShouldBe("Tedarikçisiz");
        groups[0].GetProperty("lines")[0].GetProperty("suggestedQty").GetDecimal().ShouldBe(5m);
    }

    [Test]
    public async Task Suggestions_ReadOnlyUser_NoLastPrice() {
        var env       = await NewPurchasingEnv();
        var warehouse = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "D1");
        var product   = await ProductWithSupplier(env, "A1", env.Supplier);
        await SetMinimum(env, warehouse, product, 5m);
        var reader = await OtherClient(env, "satinalma.read");

        var groups = await Suggestions(env, reader);

        groups[0].GetProperty("lines")[0].GetProperty("lastPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        using var create = await reader.PostAsJsonAsync($"{Api}/suggestions/create-orders", new { groups = Array.Empty<object>() });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
