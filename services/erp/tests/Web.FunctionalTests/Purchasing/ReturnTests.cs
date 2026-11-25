using System.Text.Json;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Purchasing.PurchasingTestSupport;

namespace ERP.Web.FunctionalTests.Purchasing;

public class ReturnTests : TestBase {
    private static Task<HttpResponseMessage> PostReturn(QuarantineTests.Setup s, decimal quantity, bool createInvoice, string reason = "Hatalı mal") {
        var receiptId = s.Receipt.GetProperty("id").GetGuid();
        return s.Env.Client.PostAsJsonAsync($"{Api}/returns", new {
            receiptId, date = Day.AddDays(2).ToString("yyyy-MM-dd"), reason, createInvoice,
            lines = new[] { new { receiptLineId = s.ReceiptLine, quantity } }
        });
    }

    [Test]
    public async Task Return_ExceedsReceived_Returns400() {
        var s = await QuarantineTests.Held(enabled: false);

        using var response = await PostReturn(s, 11m, false);

        await Fail(response, HttpStatusCode.BadRequest, "return_exceeds_received");
        (await Physical(s.Env, s.Normal)).ShouldBe(10m);
    }

    [Test]
    public async Task Return_ExceedsAcrossTwoReturns_Returns400() {
        var s = await QuarantineTests.Held(enabled: false);
        using var first = await PostReturn(s, 6m, false);

        using var second = await PostReturn(s, 5m, false);

        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        await Fail(second, HttpStatusCode.BadRequest, "return_exceeds_received");
        (await Physical(s.Env, s.Normal)).ShouldBe(4m);
    }

    [Test]
    public async Task Return_WithoutInvoice_IssuesStockOnly() {
        var s = await QuarantineTests.Held(enabled: false);

        using var response = await PostReturn(s, 4m, false);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.JsonAsync()).GetProperty("invoiceId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Physical(s.Env, s.Normal)).ShouldBe(6m);
        var movement = (await StockTestSupport.Movements(s.Env.Tenant)).Single(m => m.Type == MovementType.PurchaseReturn);
        movement.Quantity.ShouldBe(-4m);
        movement.UnitCost.ShouldBe(20m); // alış maliyetiyle çıkar
        movement.SourceLineId.ShouldBe(s.ReceiptLine);
    }

    [Test]
    public async Task Return_CreatesPurchaseReturnInvoiceDraft() {
        var s        = await QuarantineTests.Held(enabled: false);
        var original = await QuarantineTests.IssuedPurchaseInvoice(s, 10m);

        using var response = await PostReturn(s, 4m, true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var invoiceId = (await response.JsonAsync()).GetProperty("invoiceId").GetGuid();
        await using var db = PricingTestSupport.Db(s.Env.Tenant);
        var draft = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        draft.Kind.ShouldBe(InvoiceKind.PurchaseReturn);
        draft.Status.ShouldBe(InvoiceStatus.Draft);
        draft.AffectsStock.ShouldBeFalse(); // stok iade belgesinde düşer, fatura çift düşmez
        draft.ReturnOfInvoiceId.ShouldBe(original);
        draft.Lines.Single().Quantity.ShouldBe(4m);
        draft.Lines.Single().UnitPrice.ShouldBe(20m);
        (await Physical(s.Env, s.Normal)).ShouldBe(6m);
    }

    [Test]
    public async Task Return_InvoiceWithoutPurchaseInvoice_Returns409AndKeepsStock() {
        var s = await QuarantineTests.Held(enabled: false);

        using var response = await PostReturn(s, 4m, true);

        await Fail(response, HttpStatusCode.Conflict, "purchase_invoice_required");
        (await Physical(s.Env, s.Normal)).ShouldBe(10m);
    }
}
