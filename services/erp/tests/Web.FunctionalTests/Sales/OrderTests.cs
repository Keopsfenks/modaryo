using System.Text.Json;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class OrderTests : TestBase {
    private static async Task<JsonElement> PutPlan(HttpClient client, Guid id, object body, HttpStatusCode expected = HttpStatusCode.OK) {
        using var response = await client.PutAsJsonAsync($"{Api}/orders/{id}/installment-plan", body);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    [Test]
    public async Task InstallmentMismatch_Returns400() {
        var env = await NewOrderEnv();
        var id  = await NewOrder(env, [QLine(env.Product, 2m, 50m)]); // 100 + KDV = 120
        var due = Today().AddMonths(1).ToString("yyyy-MM-dd");

        using var bad = await env.Client.PutAsJsonAsync($"{Api}/orders/{id}/installment-plan", new { downPayment = 20m, rows = new[] { new { dueDate = due, amount = 50m } } });
        await Fail(bad, HttpStatusCode.BadRequest, "installment_mismatch");

        var plan = await PutPlan(env.Client, id, new { downPayment = 20m, rows = new[] { new { dueDate = due, amount = 40m }, new { dueDate = Today().AddMonths(2).ToString("yyyy-MM-dd"), amount = 60m } } });
        plan.GetProperty("plan").GetProperty("rows").GetArrayLength().ShouldBe(2);
        plan.GetProperty("plan").GetProperty("downPayment").GetDecimal().ShouldBe(20m);
    }

    [Test]
    public async Task InstallmentPlan_EqualSplit_RemainderOnLastRow() {
        var env = await NewOrderEnv();
        var id  = await NewOrder(env, [QLine(env.Product, 2m, 50m)]); // 120

        var order = await PutPlan(env.Client, id, new { downPayment = 0m, count = 7, firstDueDate = Today().AddMonths(1).ToString("yyyy-MM-dd") });

        var rows = order.GetProperty("plan").GetProperty("rows").EnumerateArray().ToList();
        rows.Count.ShouldBe(7);
        rows[0].GetProperty("amount").GetDecimal().ShouldBe(17.14m);
        rows[6].GetProperty("amount").GetDecimal().ShouldBe(17.16m);
        rows.Sum(r => r.GetProperty("amount").GetDecimal()).ShouldBe(120m);
        rows[0].GetProperty("remaining").GetDecimal().ShouldBe(17.14m);
    }

    [Test]
    public async Task Update_OnlyDraft() {
        var env = await NewOrderEnv();
        await Stock(env, 10m);
        var id = await NewOrder(env, [QLine(env.Product, 2m, 50m)]);
        using var edit = await env.Client.PutAsJsonAsync($"{Api}/orders/{id}", OrderBody(env, [QLine(env.Product, 3m, 50m)]));
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        (await edit.JsonAsync()).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(3m);

        await Approve(env.Client, id);
        using var again = await env.Client.PutAsJsonAsync($"{Api}/orders/{id}", OrderBody(env, [QLine(env.Product, 4m, 50m)]));

        await Fail(again, HttpStatusCode.Conflict, "order_not_draft");
    }

    [Test]
    public async Task Cancel_ReleasesReservations() {
        var env = await NewOrderEnv();
        await Stock(env, 10m);
        var id = await NewOrder(env, [QLine(env.Product, 4m, 50m)]);
        await Approve(env.Client, id);
        (await Reservations(env)).ShouldHaveSingleItem().Status.ToString().ShouldBe("Active");

        var cancelled = await Post(env.Client, $"orders/{id}/cancel", new { reason = "Müşteri vazgeçti" });

        Status(cancelled).ShouldBe("cancelled");
        (await Reservations(env)).ShouldAllBe(r => r.Status.ToString() == "Released");
        using var again = await env.Client.PostAsJsonAsync($"{Api}/orders/{id}/cancel", new { reason = "tekrar" });
        await Fail(again, HttpStatusCode.Conflict, "order_not_open");
    }

    [Test]
    public async Task ProfitHiddenWithoutMaliyetRead() {
        var env = await NewOrderEnv();
        await Stock(env, 10m, cost: 10m);
        var id = await NewOrder(env, [QLine(env.Product, 2m, 50m)]);
        var limited = await OtherClient(env, "satis.read", "satis.manage", "satis.approve", "cari.read", "stok.read");

        var with    = await GetOrder(env.Client, id);
        var without = await GetOrder(limited, id);

        with.GetProperty("lines")[0].GetProperty("estimatedUnitCost").GetDecimal().ShouldBe(10m);
        with.GetProperty("lines")[0].GetProperty("marginPercent").GetDecimal().ShouldBe(80m);
        with.GetProperty("marginPercent").GetDecimal().ShouldBe(80m);
        IsNullOrMissing(without.GetProperty("lines")[0], "estimatedUnitCost").ShouldBeTrue();
        IsNullOrMissing(without.GetProperty("lines")[0], "marginPercent").ShouldBeTrue();
        IsNullOrMissing(without, "estimatedCost").ShouldBeTrue();
        IsNullOrMissing(without, "marginPercent").ShouldBeTrue();
    }

    [Test]
    public async Task Pending_ListsApprovedOrderLines() {
        var env = await NewOrderEnv();
        await Stock(env, 10m);
        var id = await NewOrder(env, [QLine(env.Product, 4m, 50m)]);
        await Approve(env.Client, id);

        using var response = await env.Client.GetAsync($"{Api}/orders/pending");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var items = (await response.JsonAsync()).GetProperty("items");
        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("openToShip").GetDecimal().ShouldBe(4m);
        items[0].GetProperty("openToInvoice").GetDecimal().ShouldBe(0m);
    }

    [Test]
    public async Task Proforma_ReturnsPdf() {
        var env = await NewOrderEnv();
        var id  = await NewOrder(env);

        using var response = await env.Client.GetAsync($"{Api}/orders/{id}/proforma.pdf");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsByteArrayAsync()).AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
    }

    [Test]
    public async Task Approve_RequiresApprovePermission() {
        var env     = await NewOrderEnv();
        var id      = await NewOrder(env);
        var limited = await OtherClient(env, "satis.read", "satis.manage", "cari.read");

        using var response = await ApproveRaw(limited, id);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
