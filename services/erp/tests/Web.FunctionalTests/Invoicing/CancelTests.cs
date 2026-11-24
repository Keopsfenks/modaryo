using ERP.Domain.CurrentAccounts;
using StockEnv = ERP.Web.FunctionalTests.Inventory.StockTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Invoicing.PostingSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

public class CancelTests : TestBase {
    private static (decimal Quantity, decimal Value) LayerValue(List<ERP.Domain.Inventory.CostLayer> layers) {
        return (layers.Sum(l => l.RemainingQty), layers.Sum(l => l.RemainingQty * l.UnitCost));
    }

    [Test]
    public async Task Cancel_RevertsLedgerDueLinesAndStock() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        await StockEnv.SetStock(env.Client, "fifo");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 10m, 10m);
        var valueBefore = LayerValue(await Layers(env.Tenant));
        // 4 x 50 + %20 = 240, hizmet 220: ödenecek 460.
        var schedule = new[] { new { dueDate = "2026-04-10", amount = 100m }, new { dueDate = "2026-05-10", amount = 180m }, new { dueDate = "2026-06-10", amount = 180m } };
        // Önce bir alacak (ön ödeme): fatura kesilince vade satırlarıyla eşleştirilir.
        using var credit = await env.Client.PostAsJsonAsync($"/api/erp/current-accounts/{env.Customer}/entries",
                                                            new { type = "credit", date = "2026-03-01", amount = 50m, description = "Ön ödeme" });
        credit.StatusCode.ShouldBe(HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());
        var balanceBefore = await Balance(env, env.Customer);
        var allocBefore   = (await Allocations(env.Tenant)).Select(a => (a.DebitLineId, a.CreditLineId, a.Amount)).OrderBy(a => a.Amount).ToList();

        var issued = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 4m, 50m), FreeLine()],
                                                    extra: new { dueSchedule = schedule, affectsStock = true, warehouseId = warehouse }));
        var id = issued.GetProperty("id").GetGuid();
        (await Balance(env, env.Customer)).ShouldBe(balanceBefore + 460m);
        (await StockOf(env, warehouse)).Physical.ShouldBe(6m);
        (await DueLines(env.Tenant)).Count.ShouldBe(4);
        (await Allocations(env.Tenant)).ShouldNotBeEmpty();

        using var response = await Cancel(env.Client, id, "Yanlış cari");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await Balance(env, env.Customer)).ShouldBe(balanceBefore);
        var entry = (await Entries(env.Tenant)).Single(e => e.Source == LedgerEntrySource.Invoice);
        entry.IsCancelled.ShouldBeTrue();
        entry.CancelReason.ShouldBe("Yanlış cari");
        // Açık kalemler ve eşleştirmeler fatura öncesine döner.
        (await Allocations(env.Tenant)).Select(a => (a.DebitLineId, a.CreditLineId, a.Amount)).OrderBy(a => a.Amount).ToList().ShouldBe(allocBefore);
        using var items = await env.Client.GetAsync($"/api/erp/current-accounts/{env.Customer}/due-items");
        (await items.Content.ReadAsStringAsync()).ShouldNotContain(entry.Id.ToString());
        (await StockOf(env, warehouse)).Physical.ShouldBe(10m);
        // Ters kayıt yeni bir giriş katmanı açar; kalan miktar ve değer fatura öncesine döner.
        LayerValue(await Layers(env.Tenant)).ShouldBe(valueBefore);

        var after = await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync();
        after.GetProperty("status").GetString().ShouldBe("cancelled");
        after.GetProperty("cancelReason").GetString().ShouldBe("Yanlış cari");
        after.GetProperty("number").GetString().ShouldBe("F000001"); // numara kullanılmış kalır
        (await StockEnv.OutboxCount(env.Tenant, "invoice.cancelled")).ShouldBe(1);

        // Numara yeniden verilmez.
        var next = await CreateAndIssue(env, Body(env, lines: [FreeLine()]));
        next.GetProperty("number").GetString().ShouldBe("F000002");
    }

    [Test]
    public async Task CancelTwice_Returns409() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var id = (await CreateAndIssue(env, Body(env))).GetProperty("id").GetGuid();

        (await Cancel(env.Client, id)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var again = await Cancel(env.Client, id);

        await Fail(again, HttpStatusCode.Conflict, "invoice_cancelled");
        (await Balance(env, env.Customer)).ShouldBe(0m);
        (await StockEnv.OutboxCount(env.Tenant, "invoice.cancelled")).ShouldBe(1);
    }

    [Test]
    public async Task CancelDraft_Returns409_AndValidatesReason_AndPermission() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var draft = await Create(env, Body(env));
        var id    = draft.GetProperty("id").GetGuid();

        using var notIssued = await Cancel(env.Client, id);
        await Fail(notIssued, HttpStatusCode.Conflict, "invoice_not_issued");

        await Issue(env.Client, id);
        using var empty = await Cancel(env.Client, id, " ");
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var tooLong = await Cancel(env.Client, id, new string('x', 201));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var issuer    = await OtherClient(env, "fatura.read", "fatura.issue");
        using var forbidden = await Cancel(issuer, id);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Entries(env.Tenant)).Single().IsCancelled.ShouldBeFalse();
    }

    [Test]
    public async Task CancelPurchase_RequiresFiyatAlis() {
        var env = await NewEnv();
        await CreateSeries(env, "AF", group: "purchase");
        var id = (await CreateAndIssue(env, Body(env, "Purchase"))).GetProperty("id").GetGuid();
        using var client = await OtherClient(env, "fatura.read", "fatura.cancel");

        using var response = await Cancel(client, id);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CancelInLockedPeriod_Returns409_AndChangesNothing() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 10m);
        var id = (await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }))).GetProperty("id").GetGuid();
        await LockPeriod(env.Client, "2026-03-31");

        using var response = await Cancel(env.Client, id);

        await Fail(response, HttpStatusCode.Conflict, "period_locked");
        (await Entries(env.Tenant)).Single().IsCancelled.ShouldBeFalse();
        (await StockOf(env, warehouse)).Physical.ShouldBe(6m);
        (await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("issued");
    }

    [Test]
    public async Task CancelPurchaseWhoseStockWasConsumed_FailsAndKeepsEverything() {
        var env = await NewEnv();
        await CreateSeries(env, "AF", group: "purchase");
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        var purchase = await CreateAndIssue(env, Body(env, "Purchase", lines: [ProductLine(env, 5m, 10m)], extra: new { affectsStock = true, warehouseId = warehouse }));
        await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 4m, 20m)], extra: new { affectsStock = true, warehouseId = warehouse }));
        var id = purchase.GetProperty("id").GetGuid();

        using var response = await Cancel(env.Client, id);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await Entries(env.Tenant)).ShouldAllBe(e => !e.IsCancelled);
        (await StockOf(env, warehouse)).Physical.ShouldBe(1m);
        (await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("issued");
    }

    [Test]
    public async Task ParallelCancels_ExactlyOneWins_EffectsReversedOnce() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 10m);
        var id = (await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }))).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Cancel(env.Client, id))));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        foreach (var r in responses.Where(r => r.StatusCode != HttpStatusCode.NoContent)) await Fail(r, HttpStatusCode.Conflict, "invoice_cancelled");
        (await StockOf(env, warehouse)).Physical.ShouldBe(10m);
        (await InvoiceMovements(env.Tenant)).Count.ShouldBe(2); // çıkış + tek ters kayıt
        (await Balance(env, env.Customer)).ShouldBe(0m);
        (await StockEnv.OutboxCount(env.Tenant, "invoice.cancelled")).ShouldBe(1);
    }

    [Test]
    public async Task CancelRacingIssue_IsAllOrNothing() {
        for (var round = 0; round < 4; round++) {
            var env = await NewEnv(productCode: "R" + round);
            await CreateSeries(env, "F");
            var warehouse = await Warehouse(env);
            await Stock(env, warehouse, 10m);
            var draft = await Create(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }));
            var id    = draft.GetProperty("id").GetGuid();

            var issue  = Task.Run(() => env.Client.PostAsync($"{Url}/{id}/issue", null));
            var cancel = Task.Run(() => Cancel(env.Client, id));
            using var issueResponse  = await issue;
            using var cancelResponse = await cancel;

            issueResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await issueResponse.Content.ReadAsStringAsync());
            var status  = (await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("status").GetString();
            var entries = await Entries(env.Tenant);
            var stock   = (await StockOf(env, warehouse)).Physical;
            if (cancelResponse.StatusCode == HttpStatusCode.NoContent) {
                // Kesim önce bitti, iptal sonra: her etki geri alınmış.
                status.ShouldBe("cancelled");
                entries.Single().IsCancelled.ShouldBeTrue();
                stock.ShouldBe(10m);
                (await Balance(env, env.Customer)).ShouldBe(0m);
            } else {
                // İptal taslağı gördü: hiçbir şey iptal edilmedi, kesim tam yazıldı.
                await Fail(cancelResponse, HttpStatusCode.Conflict, "invoice_not_issued");
                status.ShouldBe("issued");
                entries.Single().IsCancelled.ShouldBeFalse();
                stock.ShouldBe(6m);
                (await Balance(env, env.Customer)).ShouldBe(240m);
            }
        }
    }

    [Test]
    public async Task ParallelIssuesAndCancels_OnSharedStock_DoNotDeadlock_AndBalance() {
        var env = await NewEnv();
        await CreateSeries(env, "F");
        var warehouse = await Warehouse(env);
        await Stock(env, warehouse, 100m);
        var issued = new List<Guid>();
        for (var i = 0; i < 6; i++)
            issued.Add((await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 2m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }))).GetProperty("id").GetGuid());
        var drafts = new List<Guid>();
        for (var i = 0; i < 6; i++)
            drafts.Add((await Create(env, Body(env, lines: [ProductLine(env, 3m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }))).GetProperty("id").GetGuid());

        var tasks = issued.Select(id => Task.Run(() => Cancel(env.Client, id)))
                          .Concat(drafts.Select(id => Task.Run(() => env.Client.PostAsync($"{Url}/{id}/issue", null))));
        var responses = await Task.WhenAll(tasks);

        responses.ShouldAllBe(r => (int)r.StatusCode < 300);
        (await StockOf(env, warehouse)).Physical.ShouldBe(100m - 6 * 3m);
        (await Balance(env, env.Customer)).ShouldBe(6 * 180m);
    }
}
