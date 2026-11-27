using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ERP.Web.FunctionalTests.Pricing;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.OrderTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;
using static ERP.Web.FunctionalTests.Sales.ShippingTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class TargetTests : TestBase {
    private static async Task<JsonElement> PutTargets(Env env, int year, params (Guid User, int Month, decimal Amount)[] rows) {
        using var response = await env.Client.PutAsJsonAsync($"{Api}/targets?year={year}", new { rows = rows.Select(r => new { userId = r.User, month = r.Month, amountTry = r.Amount }).ToArray() });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<List<JsonElement>> Actuals(Env env, int year) {
        using var response = await env.Client.GetAsync($"{Api}/targets/actuals?year={year}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("rows").EnumerateArray().ToList();
    }

    private static JsonElement Row(List<JsonElement> rows, Guid user, int month) {
        return rows.Single(r => r.GetProperty("userId").GetGuid() == user && r.GetProperty("month").GetInt32() == month);
    }

    /// <summary>Temsilcinin siparişini hızlı faturayla keser: (sipariş, fatura, fatura satırı).</summary>
    private static async Task<(Guid Order, Guid Invoice, Guid Line)> SoldBy(Env env, Guid rep, decimal quantity) {
        await Stock(env, quantity);
        var id = await NewOrder(env, [QLine(env.Product, quantity, 50m)], new { salesRepUserId = rep });
        Status(await Approve(env.Client, id)).ShouldBe("approved");
        var invoice = (await Post(env.Client, $"orders/{id}/quick-invoice", new { })).GetProperty("invoice");
        return (id, invoice.GetProperty("id").GetGuid(), invoice.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    [Test]
    public async Task Targets_PutReplacesYear_AndValidatesMembers() {
        var env  = await NewOrderEnv();
        var user = TestEnvironment.Factory.Members.Seed(env.Tenant, Guid.CreateVersion7(), "Ayşe Temsilci").UserId;

        await PutTargets(env, 2026, (user, 3, 1000m), (user, 4, 2000m));
        await PutTargets(env, 2026, (user, 3, 1500m));

        using var get = await env.Client.GetAsync($"{Api}/targets?year=2026");
        var rows = (await get.JsonAsync()).GetProperty("rows").EnumerateArray().ToList();
        var row  = rows.ShouldHaveSingleItem();
        row.GetProperty("amountTry").GetDecimal().ShouldBe(1500m);
        row.GetProperty("userName").GetString().ShouldBe("Ayşe Temsilci");
        using var stranger = await env.Client.PutAsJsonAsync($"{Api}/targets?year=2026", new { rows = new[] { new { userId = Guid.CreateVersion7(), month = 1, amountTry = 10m } } });
        await Fail(stranger, HttpStatusCode.BadRequest, "sales_rep_invalid");
        using var badMonth = await env.Client.PutAsJsonAsync($"{Api}/targets?year=2026", new { rows = new[] { new { userId = user, month = 13, amountTry = 10m } } });
        badMonth.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var duplicate = await env.Client.PutAsJsonAsync($"{Api}/targets?year=2026", new { rows = new[] { new { userId = user, month = 2, amountTry = 10m }, new { userId = user, month = 2, amountTry = 20m } } });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Targets_ActualFromIssuedInvoicesMinusReturns() {
        var env   = await NewOrderEnv();
        var ayse  = TestEnvironment.Factory.Members.Seed(env.Tenant, Guid.CreateVersion7(), "Ayşe Temsilci").UserId;
        var mehmet = TestEnvironment.Factory.Members.Seed(env.Tenant, Guid.CreateVersion7(), "Mehmet Temsilci").UserId;
        var today = Today();

        var sold  = await SoldBy(env, ayse, 5m);      // 5 x 50 = 250 net
        await SoldBy(env, mehmet, 2m);                // 2 x 50 = 100 net
        // 2 adet iade: 100 net düşer; iade faturası kesilmeden sayılmaz.
        var warehouse = await ERP.Web.FunctionalTests.Inventory.InventoryTestSupport.DefaultWarehouse(env.Client, env.Branch);
        var returned = await Post(env.Client, "returns", new {
            invoiceId = sold.Invoice, warehouseKind = "normal", warehouseId = warehouse, reason = "Müşteri vazgeçti", lines = new[] { new { invoiceLineId = sold.Line, quantity = 2m } }
        }, HttpStatusCode.Created);
        await PutTargets(env, today.Year, (ayse, today.Month, 1000m), (mehmet, today.Month, 400m));

        var before = await Actuals(env, today.Year);
        Row(before, ayse, today.Month).GetProperty("actualTry").GetDecimal().ShouldBe(250m);

        await Issue(env.Client, returned.GetProperty("returnInvoiceId").GetGuid());
        var after = await Actuals(env, today.Year);

        var a = Row(after, ayse, today.Month);
        a.GetProperty("targetTry").GetDecimal().ShouldBe(1000m);
        a.GetProperty("actualTry").GetDecimal().ShouldBe(150m);
        a.GetProperty("userName").GetString().ShouldBe("Ayşe Temsilci");
        var m = Row(after, mehmet, today.Month);
        m.GetProperty("actualTry").GetDecimal().ShouldBe(100m);
        m.GetProperty("targetTry").GetDecimal().ShouldBe(400m);
    }

    [Test]
    public async Task Targets_ManualInvoiceCountsForCariDefaultRep_DraftsAndOtherYearsDoNot() {
        var env  = await NewOrderEnv();
        var user = TestEnvironment.Factory.Members.Seed(env.Tenant, Guid.CreateVersion7(), "Ayşe Temsilci").UserId;
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var cari = await db.CurrentAccounts.SingleAsync(c => c.Id == env.Customer);
            cari.SalesRepUserId = user;
            await db.SaveChangesAsync();
        }

        var date = Today();
        await CreateAndIssue(env, Body(env, "Sales", [FreeLine("Hizmet", 1m, 300m, 20)], null, date));
        await Create(env, Body(env, "Sales", [FreeLine("Hizmet", 1m, 999m, 20)], null, date));                       // taslak sayılmaz
        await CreateAndIssue(env, Body(env, "Sales", [FreeLine("Hizmet", 1m, 700m, 20)], null, new DateOnly(date.Year - 1, 6, 1))); // başka yıl

        var rows = await Actuals(env, date.Year);

        Row(rows, user, date.Month).GetProperty("actualTry").GetDecimal().ShouldBe(300m);
        rows.Count.ShouldBe(1);
    }
}
