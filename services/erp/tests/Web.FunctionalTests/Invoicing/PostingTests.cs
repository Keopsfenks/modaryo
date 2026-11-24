using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.PostingSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class PostingTests : TestBase {
    // Varsayılan gövde: 2 x 50 + %20 KDV = 120, hizmet 200 + %10 KDV = 220; ödenecek 340.

    [Test]
    public async Task IssueSales_DebitsCariWithDocumentNo() {
        var env = await NewEnv();
        await CreateSeries(env, "F");

        var issued = await CreateAndIssue(env, Body(env));

        var entry = (await Entries(env.Tenant)).Single();
        entry.Debit.ShouldBe(340m);
        entry.Credit.ShouldBe(0m);
        entry.Source.ShouldBe(LedgerEntrySource.Invoice);
        entry.SourceId.ShouldBe(issued.GetProperty("id").GetGuid());
        entry.DocumentNo.ShouldBe("F000001");
        entry.Date.ShouldBe(Day);
        entry.Currency.ShouldBe("TRY");
        entry.SourceCurrency.ShouldBeNull();
        (await Balance(env, env.Customer)).ShouldBe(340m);

        // Vade planı verilmedi: tek satır, fatura tarihi + cari vade günü (30).
        var due = (await DueLines(env.Tenant)).Single();
        due.DueDate.ShouldBe(Day.AddDays(30));
        due.Amount.ShouldBe(340m);
        due.Direction.ShouldBe(DueDirection.Receivable);
        entry.DueDate.ShouldBe(Day.AddDays(30));
    }

    [Test]
    public async Task IssuePurchase_CreditsCari() {
        var env = await NewEnv();
        await CreateSeries(env, "AF", group: "purchase");

        await CreateAndIssue(env, Body(env, "Purchase"));

        var entry = (await Entries(env.Tenant)).Single();
        entry.Credit.ShouldBe(340m);
        entry.Debit.ShouldBe(0m);
        (await Balance(env, env.Supplier)).ShouldBe(-340m);
        (await DueLines(env.Tenant)).Single().Direction.ShouldBe(DueDirection.Payable);
    }

    [Test]
    public async Task IssueReturns_ReverseTheDirection() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        await CreateSeries(env, "AF", group: "purchase");
        var sale     = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 2m, 50m)]));
        var purchase = await CreateAndIssue(env, Body(env, "Purchase", lines: [ProductLine(env, 2m, 50m)]));
        var saleLine     = sale.GetProperty("lines")[0].GetProperty("id").GetGuid();
        var purchaseLine = purchase.GetProperty("lines")[0].GetProperty("id").GetGuid();

        await CreateAndIssue(env, Body(env, "SalesReturn", lines: [ProductLine(env, 1m, 50m, new { sourceLineId = saleLine })],
                                       extra: new { returnOfInvoiceId = sale.GetProperty("id").GetGuid() }));
        await CreateAndIssue(env, Body(env, "PurchaseReturn", lines: [ProductLine(env, 1m, 50m, new { sourceLineId = purchaseLine })],
                                       extra: new { returnOfInvoiceId = purchase.GetProperty("id").GetGuid() }));

        (await Balance(env, env.Customer)).ShouldBe(60m);  // 120 borç - 60 alacak
        (await Balance(env, env.Supplier)).ShouldBe(-60m); // 120 alacak - 60 borç
    }

    [Test]
    public async Task IssueWithSchedule_CreatesThreeDueLines() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var schedule = new[] {
            new { dueDate = "2026-05-10", amount = 100m }, new { dueDate = "2026-04-10", amount = 120m }, new { dueDate = "2026-06-10", amount = 120m }
        };

        await CreateAndIssue(env, Body(env, extra: new { dueSchedule = schedule }));

        var due = await DueLines(env.Tenant);
        due.Select(d => (d.Sequence, d.DueDate, d.Amount)).ToList().ShouldBe([
            (1, new DateOnly(2026, 4, 10), 120m), (2, new DateOnly(2026, 5, 10), 100m), (3, new DateOnly(2026, 6, 10), 120m)
        ]);
        (await Entries(env.Tenant)).Single().DueDate.ShouldBe(new DateOnly(2026, 4, 10));
    }

    [Test]
    public async Task ScheduleMismatch_Returns400() {
        var env = await NewEnv();
        await CreateSeries(env, "F");

        using var response = await env.Client.PostAsJsonAsync(Url, Body(env, extra: new { dueSchedule = new[] { new { dueDate = "2026-05-10", amount = 339m } } }));

        await Fail(response, HttpStatusCode.BadRequest, "due_schedule_mismatch");
        (await Entries(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Posting_UsdInvoiceToTryCari_AndTryInvoiceToUsdCari() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        // USD fatura (kur 30), TL cari: TL karşılığı yazılır.
        var usd = await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], extra: new { currency = "USD", rate = 30m }));
        Dec(usd, "totals", "payable").ShouldBe(120m);

        var first = (await Entries(env.Tenant)).Single();
        first.Currency.ShouldBe("TRY");
        first.Debit.ShouldBe(3600m);
        first.SourceCurrency.ShouldBe("USD");
        first.SourceAmount.ShouldBe(120m);
        first.Rate.ShouldBe(30m);
        (await DueLines(env.Tenant)).Single().Amount.ShouldBe(3600m);

        // TL fatura, USD cari: aynı gün kuruyla USD yazılır.
        var usdCari = await CreateCari(env.Client, "M2", customer: true, supplier: false, currency: "USD");
        await Override(env, "USD", "2026-03-10", 40m);
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 1000m, vatRate: 20)], extra: new { currentAccountId = usdCari }));

        var second = (await Entries(env.Tenant)).Single(e => e.CurrentAccountId == usdCari);
        second.Currency.ShouldBe("USD");
        second.Debit.ShouldBe(30m); // 1200 TL / 40
        second.SourceCurrency.ShouldBe("TRY");
        second.SourceAmount.ShouldBe(1200m);
        second.Rate.ShouldBe(40m);
        (await DueLines(env.Tenant)).Single(d => d.CurrentAccountId == usdCari).Amount.ShouldBe(30m);
    }

    [Test]
    public async Task IssueSalesAffectsStock_IssuesStockAtCost() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 10m, 10m);

        var issued = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }));

        (await StockOf(env, warehouse)).Physical.ShouldBe(6m);
        var movement = (await InvoiceMovements(env.Tenant)).Single();
        movement.Type.ShouldBe(MovementType.SalesShipment);
        movement.Quantity.ShouldBe(-4m);
        movement.UnitCost.ShouldBe(10m);
        movement.SourceId.ShouldBe(issued.GetProperty("id").GetGuid());
        movement.SourceLineId.ShouldBe(issued.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    [Test]
    public async Task IssueSalesAffectsStock_InsufficientStock_RollsEverythingBack() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 1m);
        var draft = await Create(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }));
        var id    = draft.GetProperty("id").GetGuid();

        using var response = await env.Client.PostAsync($"{Url}/{id}/issue", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await Entries(env.Tenant)).ShouldBeEmpty();
        (await DueLines(env.Tenant)).ShouldBeEmpty();
        (await StockOf(env, warehouse)).Physical.ShouldBe(1m);
        (await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("draft");
        // Numara harcanmadı.
        (await (await env.Client.GetAsync(SeriesUrl)).JsonAsync())[0].GetProperty("nextPreview").GetString().ShouldBe("F000001");
    }

    [Test]
    public async Task IssuePurchaseAffectsStock_ReceivesAtNetTlCost() {
        var env = await NewEnv();
        await CreateSeries(env, "AF", group: "purchase");
        var warehouse = await Warehouse(env);

        // 4 adet x 25 USD, %10 iskonto, %20 KDV: net 90 USD = 2700 TL; birim maliyet 675 TL (KDV hariç).
        await CreateAndIssue(env, Body(env, "Purchase", lines: [ProductLine(env, 4m, 25m, new { discountPercent = 10m })],
                                       extra: new { currency = "USD", rate = 30m, affectsStock = true, warehouseId = warehouse }));

        var movement = (await InvoiceMovements(env.Tenant)).Single();
        movement.Type.ShouldBe(MovementType.PurchaseReceipt);
        movement.Quantity.ShouldBe(4m);
        movement.UnitCost.ShouldBe(675m);
        (await StockOf(env, warehouse)).Physical.ShouldBe(4m);
    }

    [Test]
    public async Task IssueServiceLinesOnly_DoNotTouchStock() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);

        await CreateAndIssue(env, Body(env, lines: [FreeLine()], extra: new { affectsStock = true, warehouseId = warehouse }));

        (await InvoiceMovements(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task CariStatementShowsInvoiceNumberAndSourceLink() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var issued = await CreateAndIssue(env, Body(env));

        using var response = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}/statement?from=2026-01-01&to=2026-12-31");
        var row = (await response.JsonAsync()).GetProperty("rows")[0];

        row.GetProperty("documentNo").GetString().ShouldBe("F000001");
        row.GetProperty("source").GetString().ShouldBe("invoice");
        row.GetProperty("sourceId").GetGuid().ShouldBe(issued.GetProperty("id").GetGuid());

        // Cari ekranından fatura hareketi iptal edilemez.
        var entry = (await Entries(env.Tenant)).Single();
        using var cancel = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/entries/{entry.Id}/cancel", new { reason = "Deneme" });
        await Fail(cancel, HttpStatusCode.Conflict, "not_manual");
    }

    [Test]
    public async Task IssueEnqueuesInvoiceIssuedEvent() {
        var env = await NewEnv();
        await CreateSeries(env, "F");

        var issued = await CreateAndIssue(env, Body(env));

        await using var db = PricingTestSupport.Db(env.Tenant);
        var message = db.OutboxMessages.Where(m => m.Type == "invoice.issued").ToList().Single();
        using var json = JsonDocument.Parse(message.PayloadJson);
        json.RootElement.GetProperty("invoice_id").GetGuid().ShouldBe(issued.GetProperty("id").GetGuid());
        json.RootElement.GetProperty("number").GetString().ShouldBe("F000001");
        json.RootElement.GetProperty("kind").GetString().ShouldBe("Sales");
        json.RootElement.GetProperty("mode").GetString().ShouldBe("Paper");
    }
}
