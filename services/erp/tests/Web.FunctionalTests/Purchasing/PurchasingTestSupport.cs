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

    public static Task<Env> NewPurchasingEnv(string[]? permissions = null) {
        return NewEnv(permissions ?? Permissions);
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
