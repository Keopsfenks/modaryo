using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

internal static class ShippingTestSupport {
    public static Guid LineId(JsonElement order, int index = 0) => order.GetProperty("lines")[index].GetProperty("id").GetGuid();

    public static decimal LineQty(JsonElement order, string name, int index = 0) => order.GetProperty("lines")[index].GetProperty(name).GetDecimal();

    /// <summary>Stoklu, onaylı tek satırlı sipariş (KDV hariç fiyat; varsayılan depoya stok girilir).</summary>
    public static async Task<(Guid Id, JsonElement Order)> ApprovedOrder(Env env, decimal quantity, decimal price = 50m, object? lineExtra = null, object? extra = null, decimal? stock = null) {
        await Stock(env, stock ?? quantity);
        var id = await NewOrder(env, [QLine(env.Product, quantity, price, lineExtra)], extra);
        var order = await Approve(env.Client, id);
        Status(order).ShouldBe("approved");
        return (id, order);
    }

    public static object ShipBody(Guid order, IEnumerable<(Guid Line, decimal Quantity)> lines, object? extra = null) {
        var body = new Dictionary<string, object?> { ["orderId"] = order, ["lines"] = lines.Select(l => new { orderLineId = l.Line, quantity = l.Quantity }).ToArray() };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return body;
    }

    public static Task<HttpResponseMessage> ShipRaw(HttpClient client, Guid order, Guid line, decimal quantity, object? extra = null) {
        return client.PostAsJsonAsync($"{Api}/shipments", ShipBody(order, [(line, quantity)], extra));
    }

    public static async Task<JsonElement> Ship(HttpClient client, Guid order, Guid line, decimal quantity, object? extra = null) {
        using var response = await ShipRaw(client, order, line, quantity, extra);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> DraftRaw(HttpClient client, params Guid[] shipments) {
        return client.PostAsJsonAsync($"{Api}/shipments/invoice-draft", new { shipmentIds = shipments });
    }

    public static async Task<JsonElement> Draft(HttpClient client, params Guid[] shipments) {
        using var response = await DraftRaw(client, shipments);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> GetInvoice(HttpClient client, Guid id) {
        using var response = await client.GetAsync($"{Url}/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    /// <summary>Taslak faturayı verilen miktarla günceller (sevk satırı bağı korunur).</summary>
    public static Task<HttpResponseMessage> PutDraft(Env env, JsonElement draft, decimal quantity, object? extra = null) {
        var lines = draft.GetProperty("lines").EnumerateArray().Select(l => new Dictionary<string, object?> {
            ["productId"] = l.GetProperty("productId").GetGuid(), ["description"] = l.GetProperty("description").GetString(), ["unit"] = l.GetProperty("unit").GetString(),
            ["quantity"] = quantity, ["unitPrice"] = l.GetProperty("unitPrice").GetDecimal(), ["vatIncluded"] = l.GetProperty("vatIncluded").GetBoolean(),
            ["vatRate"] = l.GetProperty("vatRate").GetInt32(), ["sourceLineId"] = l.GetProperty("sourceLineId").GetGuid()
        }).ToArray();
        var body = new Dictionary<string, object?> {
            ["kind"] = "Sales", ["branchId"] = env.Branch, ["currentAccountId"] = env.Customer, ["date"] = draft.GetProperty("date").GetString(), ["currency"] = "TRY",
            ["affectsStock"] = false, ["version"] = draft.GetProperty("version").GetUInt32(), ["lines"] = lines
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return env.Client.PutAsJsonAsync($"{Url}/{draft.GetProperty("id").GetGuid()}", body);
    }

    public static async Task<JsonElement> PutDraftOk(Env env, JsonElement draft, decimal quantity) {
        using var response = await PutDraft(env, draft, quantity);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> Plan(Env env, Guid order, decimal downPayment, params (DateOnly Due, decimal Amount)[] rows) {
        using var response = await env.Client.PutAsJsonAsync($"{Api}/orders/{order}/installment-plan",
                                                             new { downPayment, rows = rows.Select(r => new { dueDate = r.Due.ToString("yyyy-MM-dd"), amount = r.Amount }).ToArray() });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static IEnumerable<(DateOnly Due, decimal Amount)> DueRows(JsonElement invoice) {
        return invoice.GetProperty("dueSchedule").EnumerateArray().Select(d => (DateOnly.Parse(d.GetProperty("dueDate").GetString()!), d.GetProperty("amount").GetDecimal())).ToList();
    }

    public static async Task<List<StockMovement>> Movements(Env env, string sourceType) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.StockMovements.AsNoTracking().Where(m => m.SourceType == sourceType).OrderBy(m => m.Sequence).ToListAsync();
    }

    public static async Task<List<Reservation>> OrderReservations(Env env) => await Reservations(env);

    public static async Task<List<Invoice>> Invoices(Env env) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.Invoices.AsNoTracking().Include(i => i.Lines).OrderBy(i => i.Created).ToListAsync();
    }

    public static async Task<SalesOrder> OrderRow(Env env, Guid id) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return await db.SalesOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Plan).ThenInclude(p => p!.Rows).SingleAsync(o => o.Id == id);
    }

    public static async Task<decimal> Physical(Env env, Guid warehouse) {
        await using var db = PricingTestSupport.Db(env.Tenant);
        return (await db.StockMovements.AsNoTracking().Where(m => m.WarehouseId == warehouse && m.ProductId == env.Product).ToListAsync()).Sum(m => m.Quantity);
    }
}
