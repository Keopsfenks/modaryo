using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class ReturnTests : TestBase {
    /// <summary>Hızlı faturayla kesilmiş fatura: (sipariş, fatura, fatura satırı, depo).</summary>
    private static async Task<(Guid Order, Guid Invoice, Guid Line, Guid Warehouse)> Sold(Env env, decimal quantity) {
        var (id, _) = await ApprovedOrder(env, quantity);
        var result  = await Post(env.Client, $"orders/{id}/quick-invoice", new { });
        var invoice = result.GetProperty("invoice");
        return (id, invoice.GetProperty("id").GetGuid(), invoice.GetProperty("lines")[0].GetProperty("id").GetGuid(), await InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch));
    }

    private static object ReturnBody(Guid invoice, Guid line, decimal quantity, Guid warehouse, string kind = "normal", string? reason = "Müşteri vazgeçti") {
        return new { invoiceId = invoice, warehouseKind = kind, warehouseId = warehouse, reason, lines = new[] { new { invoiceLineId = line, quantity } } };
    }

    private static Task<HttpResponseMessage> ReturnRaw(Env env, object body) => env.Client.PostAsJsonAsync($"{Api}/returns", body);

    [Test]
    public async Task Return_ExceedsInvoiced_Returns400() {
        var env = await NewOrderEnv();
        var sold = await Sold(env, 5m);
        using var tooMany = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 6m, sold.Warehouse));
        await Fail(tooMany, HttpStatusCode.BadRequest, "return_exceeds_invoiced");

        using var ok = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 3m, sold.Warehouse));
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());

        // Önceki iade düşülür: kalan 2.
        using var second = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 3m, sold.Warehouse));
        await Fail(second, HttpStatusCode.BadRequest, "return_exceeds_invoiced");
        using var rest = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 2m, sold.Warehouse));
        rest.StatusCode.ShouldBe(HttpStatusCode.Created, await rest.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Return_ReceivesStockAtOriginalCost_AndCreatesReturnDraft() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m); // 5 birim maliyet 10 ile çıktı
        await Stock(env, 10m, cost: 30m); // ortalama maliyet değişir
        using var response = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 2m, sold.Warehouse));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.JsonAsync();

        created.GetProperty("number").GetString()!.ShouldMatch(@"^IA2026\d{6}$");
        created.GetProperty("status").GetString().ShouldBe("posted");
        var movement = (await Movements(env, "sales-return")).ShouldHaveSingleItem();
        movement.Type.ShouldBe(MovementType.SalesReturn);
        movement.Quantity.ShouldBe(2m);
        movement.UnitCost.ShouldBe(10m);

        var draft = await GetInvoice(env.Client, created.GetProperty("returnInvoiceId").GetGuid());
        draft.GetProperty("kind").GetString().ShouldBe("salesReturn");
        draft.GetProperty("status").GetString().ShouldBe("draft");
        draft.GetProperty("affectsStock").GetBoolean().ShouldBeFalse();
        draft.GetProperty("returnOfInvoiceId").GetGuid().ShouldBe(sold.Invoice);
        draft.GetProperty("totals").GetProperty("payable").GetDecimal().ShouldBe(120m); // 2 x 50 + KDV
        (await GetOrder(env.Client, sold.Order)).GetProperty("lines")[0].GetProperty("returnedQuantity").GetDecimal().ShouldBe(2m);

        // Taslak iade faturası kesilebilir; stok ikinci kez girmez.
        await Issue(env.Client, draft.GetProperty("id").GetGuid());
        (await Movements(env, "invoice")).ShouldBeEmpty();
        (await Movements(env, "sales-return")).Count.ShouldBe(1);
    }

    [Test]
    public async Task Return_ToQuarantine_NotAvailable() {
        var env        = await NewOrderEnv();
        var sold       = await Sold(env, 5m); // stok 0
        var quarantine = await InventoryTestSupport.CreateWarehouse(env.Client, env.Branch, "Q1", "Quarantine");

        using var wrongKind = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 5m, sold.Warehouse, "quarantine"));
        await Fail(wrongKind, HttpStatusCode.BadRequest, "warehouse_kind_mismatch");

        using var response = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 5m, quarantine, "quarantine"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        (await Physical(env, quarantine)).ShouldBe(5m);
        var next = await NewOrder(env, [QLine(env.Product, 1m, 50m)]);
        using var approve = await ApproveRaw(env.Client, next); // karantinadaki stok satılabilir değil
        await Fail(approve, HttpStatusCode.Conflict, "insufficient_stock");
    }

    [Test]
    public async Task Return_ToNormalWarehouse_IsAvailableAgain() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m);
        using var response = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 5m, sold.Warehouse));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var next = await NewOrder(env, [QLine(env.Product, 1m, 50m)]);

        Status(await Approve(env.Client, next)).ShouldBe("approved");
    }

    [Test]
    public async Task CancelReturn_ReversesStockAndDeletesDraft() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m);
        using var response = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 4m, sold.Warehouse));
        var created = await response.JsonAsync();
        var id      = created.GetProperty("id").GetGuid();

        var draftId = created.GetProperty("returnInvoiceId").GetGuid();
        using var deleteDraft = await env.Client.DeleteAsync($"{Url}/{draftId}");
        await Fail(deleteDraft, HttpStatusCode.Conflict, "return_invoice_locked");

        var cancelled = await Post(env.Client, $"returns/{id}/cancel", new { reason = "Yanlış girildi" });

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        (await Physical(env, sold.Warehouse)).ShouldBe(0m);
        (await GetOrder(env.Client, sold.Order)).GetProperty("lines")[0].GetProperty("returnedQuantity").GetDecimal().ShouldBe(0m);
        using var gone = await env.Client.GetAsync($"{Url}/{draftId}");
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var again = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 5m, sold.Warehouse)); // kota geri geldi
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task CancelReturn_AfterReturnInvoiceIssued_Returns409_ThenInvoiceCancelRevertsReturn() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m);
        using var response = await ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 5m, sold.Warehouse));
        var created = await response.JsonAsync();
        var id      = created.GetProperty("id").GetGuid();
        var invoice = created.GetProperty("returnInvoiceId").GetGuid();
        await Issue(env.Client, invoice);

        using var blocked = await env.Client.PostAsJsonAsync($"{Api}/returns/{id}/cancel", new { reason = "Vazgeçildi" });
        await Fail(blocked, HttpStatusCode.Conflict, "return_invoice_issued");

        using var cancel = await env.Client.PostAsJsonAsync($"{Url}/{invoice}/cancel", new { reason = "İade faturası hatalı" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        using var detail = await env.Client.GetAsync($"{Api}/returns/{id}");
        (await detail.JsonAsync()).GetProperty("status").GetString().ShouldBe("cancelled");
        (await Physical(env, sold.Warehouse)).ShouldBe(0m);
        (await GetOrder(env.Client, sold.Order)).GetProperty("lines")[0].GetProperty("returnedQuantity").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task ConcurrentReturns_CannotExceedInvoiced() {
        var env  = await NewOrderEnv();
        var sold = await Sold(env, 5m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ReturnRaw(env, ReturnBody(sold.Invoice, sold.Line, 3m, sold.Warehouse))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(3);
        (await Physical(env, sold.Warehouse)).ShouldBe(3m);
    }

    [Test]
    public async Task Return_OfManualStockInvoice_UsesInvoiceIssueCost() {
        var env       = await NewOrderEnv();
        var warehouse = await Stock(env, 10m);
        var invoice   = await CreateAndIssue(env, Body(env, "Sales", [ProductLine(env, 4m, 50m)], new { affectsStock = true, warehouseId = warehouse }));
        var line      = invoice.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await Stock(env, 10m, cost: 40m);

        using var response = await ReturnRaw(env, new { invoiceId = invoice.GetProperty("id").GetGuid(), warehouseKind = "normal", warehouseId = warehouse, lines = new[] { new { invoiceLineId = line, quantity = 1m } } });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await Movements(env, "sales-return")).Single().UnitCost.ShouldBe(10m);
        (await response.JsonAsync()).GetProperty("salesOrderId").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
