using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Inventory.InventoryTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using StockEnv = ERP.Web.FunctionalTests.Inventory.StockTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

internal static class PostingSupport {
    public static async Task<Guid> Warehouse(Env env) {
        return await DefaultWarehouse(env.Client, env.Branch);
    }

    /// <summary>Depoya doğrudan stok girişi (stok servisi test ucu).</summary>
    public static async Task Stock(Env env, Guid warehouse, decimal quantity, decimal cost = 10m, Guid? product = null) {
        await StockEnv.Ok(StockEnv.Op(env.Client, "receive", StockEnv.Doc("Receipt", new DateOnly(2026, 3, 1), StockEnv.Line(warehouse, product ?? env.Product, quantity, cost))));
    }

    public static async Task<(decimal Physical, decimal? Average)> StockOf(Env env, Guid warehouse, Guid? product = null) {
        using var response = await StockEnv.Op(env.Client, "balance", new { items = new[] { new { productId = product ?? env.Product, variantId = (Guid?)null } }, warehouseId = warehouse });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = (await response.JsonAsync())[0];
        var avg = row.GetProperty("averageCost");
        return (row.GetProperty("physical").GetDecimal(), avg.ValueKind == JsonValueKind.Null ? null : avg.GetDecimal());
    }

    public static async Task<List<StockMovement>> InvoiceMovements(Guid tenant) {
        return (await StockEnv.Movements(tenant)).Where(m => m.SourceType == "invoice").ToList();
    }

    public static async Task<List<LedgerEntry>> Entries(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.LedgerEntries.AsNoTracking().OrderBy(e => e.Created).ToListAsync();
    }

    public static async Task<List<LedgerDueLine>> DueLines(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.LedgerDueLines.AsNoTracking().OrderBy(l => l.DueDate).ThenBy(l => l.Sequence).ToListAsync();
    }

    public static async Task<List<LedgerAllocation>> Allocations(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.LedgerAllocations.AsNoTracking().ToListAsync();
    }

    public static async Task<List<CostLayer>> Layers(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.CostLayers.AsNoTracking().ToListAsync();
    }

    public static async Task<decimal> Balance(Env env, Guid cari) {
        using var response = await env.Client.GetAsync($"/api/erp/current-accounts/{cari}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("balance").GetDecimal();
    }

    public static Task<HttpResponseMessage> Cancel(HttpClient client, Guid id, string reason = "Hatalı fatura") {
        return client.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason });
    }

    public static async Task Override(Env env, string currency, string date, decimal rate) {
        using var response = await env.Client.PutAsJsonAsync("/api/erp/exchange-rates/override", new { date, currency, type = "ForexBuying", rate });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
