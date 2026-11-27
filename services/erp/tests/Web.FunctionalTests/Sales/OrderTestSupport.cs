using System.Text.Json;
using ERP.Domain.Sales;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Purchasing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

internal static class OrderTestSupport {
    public const string BackToBack = "siparise-gore-tedarik";

    public static readonly string[] OrderPermissions = [
        .. Permissions, .. PurchasingTestSupport.Permissions, .. TreasuryTestSupport.Permissions
    ];

    public static Task<Env> NewOrderEnv(string[]? permissions = null, string[]? modules = null) {
        return NewEnv((permissions ?? OrderPermissions).Distinct().ToArray(), null, modules: modules);
    }

    public static Dictionary<string, object?> OrderBody(Env env, object[]? lines = null, object? extra = null) {
        return QuoteBody(env, lines, extra, Today());
    }

    public static async Task<JsonElement> CreateOrder(Env env, Dictionary<string, object?> body) {
        using var response = await env.Client.PostAsJsonAsync($"{Api}/orders", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<Guid> NewOrder(Env env, object[]? lines = null, object? extra = null) {
        return (await CreateOrder(env, OrderBody(env, lines, extra))).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> GetOrder(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Api}/orders/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> ApproveRaw(HttpClient client, Guid id) {
        return client.PostAsJsonAsync($"{Api}/orders/{id}/approve", new { });
    }

    public static async Task<JsonElement> Approve(HttpClient client, Guid id) {
        using var response = await ApproveRaw(client, id);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static string Status(JsonElement order) => order.GetProperty("status").GetString()!;

    /// <summary>Varsayılan depoya stok girişi yazar (test probu) ve depoyu döner.</summary>
    public static async Task<Guid> Stock(Env env, decimal quantity, Guid? product = null, decimal cost = 10m) {
        var warehouse = await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        await StockTestSupport.Ok(StockTestSupport.Op(env.Client, "receive",
                                                      StockTestSupport.Doc("Receipt", StockTestSupport.D1, StockTestSupport.Line(warehouse, product ?? env.Product, quantity, cost))));
        return warehouse;
    }

    public static async Task<List<ERP.Domain.Inventory.Reservation>> Reservations(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.Reservations.AsNoTracking().Where(r => r.SourceType == "sales-order").ToListAsync();
    }

    public static async Task SetCreditLimit(Env env, decimal limit) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        var cari = await db.CurrentAccounts.SingleAsync(c => c.Id == env.Customer);
        cari.CreditLimit = limit;
        await db.SaveChangesAsync();
    }

    public static async Task Debit(Env env, decimal amount) {
        using var response = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/entries", new { type = "debit", date = Today().ToString("yyyy-MM-dd"), amount });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static async Task<List<SalesOrder>> Orders(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.SalesOrders.AsNoTracking().Include(o => o.Lines).ToListAsync();
    }

    public static bool IsNullOrMissing(JsonElement element, string name) {
        return !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;
    }
}
