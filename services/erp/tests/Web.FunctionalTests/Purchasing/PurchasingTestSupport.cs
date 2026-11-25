using System.Text.Json;
using ERP.Domain.Purchasing;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

internal static class PurchasingTestSupport {
    public const string Api = "/api/erp/purchasing";

    public static readonly string[] Permissions = [
        .. AllPermissions, "satinalma.read", "satinalma.manage", "satinalma.receive", "satinalma.quarantine", "ayarlar.satinalma", "fiyat.read", "fiyat.manage"
    ];

    public static Task<Env> NewPurchasingEnv(string[]? permissions = null, string[]? modules = null) {
        return NewEnv(permissions ?? Permissions, modules: modules);
    }

    /// <summary>Mal kabul gövdesi; satır başına (poLineId, productId, quantity) verilir.</summary>
    public static Dictionary<string, object?> ReceiptBody(Env env, Guid poId, Guid warehouse, object[] lines, DateOnly? date = null, object? extra = null) {
        var body = new Dictionary<string, object?> {
            ["purchaseOrderId"] = poId, ["supplierId"] = env.Supplier, ["branchId"] = env.Branch, ["warehouseId"] = warehouse,
            ["date"] = (date ?? Day).ToString("yyyy-MM-dd"), ["lines"] = lines
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static Dictionary<string, object?> RLine(Guid poLine, Guid product, decimal quantity, object? extra = null) {
        var line = new Dictionary<string, object?> { ["poLineId"] = poLine, ["productId"] = product, ["quantity"] = quantity };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                line[property.Name] = property.GetValue(extra);
        return line;
    }

    public static Task<HttpResponseMessage> PostReceipt(HttpClient client, Dictionary<string, object?> body) {
        return client.PostAsJsonAsync($"{Api}/receipts", body);
    }

    public static async Task<JsonElement> Receive(HttpClient client, Dictionary<string, object?> body) {
        using var response = await PostReceipt(client, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task SetPurchasingSettings(HttpClient client, decimal overDeliveryPercent = 0m, bool quarantine = false, Dictionary<Guid, Guid>? quarantineWarehouses = null) {
        using var current = await client.GetAsync("/api/erp/settings/satinalma");
        var doc     = await current.JsonAsync();
        var version = doc.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
        using var put = await client.PutAsJsonAsync("/api/erp/settings/satinalma", new {
            version, value = new {
                overDeliveryPercent, priceVariancePercent = 2m, quarantineEnabled = quarantine,
                quarantineWarehouseByBranch = quarantineWarehouses ?? new Dictionary<Guid, Guid>(), defaultLandedKey = "amount"
            }
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }

    public static async Task<decimal> Physical(Env env, Guid warehouse, Guid? product = null) {
        using var response = await env.Client.PostAsJsonAsync("/test-probe/stock?op=balance", new { items = new[] { new { productId = product ?? env.Product, variantId = (Guid?)null } }, warehouseId = warehouse });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rows = await response.JsonAsync();
        return rows.GetArrayLength() == 0 ? 0m : rows[0].GetProperty("physical").GetDecimal();
    }

    public static async Task<decimal> Available(Env env, Guid warehouse, Guid? product = null) {
        using var response = await env.Client.PostAsJsonAsync("/test-probe/stock?op=balance", new { items = new[] { new { productId = product ?? env.Product, variantId = (Guid?)null } }, warehouseId = warehouse });
        var rows = await response.JsonAsync();
        return rows.GetArrayLength() == 0 ? 0m : rows[0].GetProperty("available").GetDecimal();
    }

    public static Dictionary<string, object?> PoLine(Guid product, decimal quantity = 10m, decimal? price = 20m, object? extra = null) {
        var line = new Dictionary<string, object?> { ["productId"] = product, ["quantity"] = quantity, ["unitPrice"] = price };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                line[property.Name] = property.GetValue(extra);
        return line;
    }

    public static Dictionary<string, object?> PoBody(Env env, object[]? lines = null, object? extra = null, DateOnly? date = null) {
        var body = new Dictionary<string, object?> {
            ["supplierId"] = env.Supplier, ["branchId"] = env.Branch, ["date"] = (date ?? Day).ToString("yyyy-MM-dd"), ["currency"] = "TRY",
            ["lines"] = lines ?? [PoLine(env.Product)]
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static async Task<JsonElement> CreatePo(Env env, Dictionary<string, object?> body) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> Post(HttpClient client, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) {
        using var response = await client.PostAsJsonAsync($"{Api}/{path}", body ?? new { });
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.NoContent ? default : await response.JsonAsync();
    }

    public static Task<JsonElement> Approve(HttpClient client, Guid id) => Post(client, $"orders/{id}/approve");

    public static async Task<JsonElement> GetPo(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Api}/orders/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    /// <summary>Mal kabul uçları sonraki görevlerde gelir; şimdilik mal kabul ve sipariş durumu doğrudan veritabanına yazılır.</summary>
    public static async Task SeedReceiptAsync(Env env, Guid poId, Guid warehouseId, PurchaseOrderStatus status = PurchaseOrderStatus.PartiallyReceived) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        db.GoodsReceipts.Add(new GoodsReceipt {
            Number = "MK" + Guid.NewGuid().ToString("N")[..10], PurchaseOrderId = poId, SupplierId = env.Supplier, BranchId = env.Branch,
            WarehouseId = warehouseId, Date = Day
        });
        var po = await db.PurchaseOrders.FindAsync(poId);
        po!.Status = status;
        await db.SaveChangesAsync();
    }
}
