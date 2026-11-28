using ERP.Domain.CurrentAccounts;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class VeresiyeTests : TestBase {
    private static async Task<Guid> Customer(Env env, string code, decimal? creditLimit = null) {
        using var response = await env.Client.PostAsJsonAsync("/api/erp/current-accounts", new { code, name = "Müşteri " + code, kind = "Individual", isCustomer = true, creditLimit });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<decimal> CariBalance(Guid tenant, Guid cari) {
        await using var db = PricingTestSupport.Db(tenant);
        return Statement.Balance(await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == cari && e.CancelledAt == null).ToListAsync());
    }

    [Test]
    public async Task Veresiye_DebitsCustomer() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product  = await Product(env.Client, "VER1", price: 100m);
        await ReceiveStock(env, product, 10m);
        var customer = await Customer(env, "MUS1");

        // 240: 100 nakit + 140 veresiye.
        var sale = await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 100m), Pay("onAccount", 140m)], customerId: customer);
        sale.GetProperty("total").GetDecimal().ShouldBe(240m);
        sale.GetProperty("change").GetDecimal().ShouldBe(0m);
        (await CariBalance(env.Tenant, customer)).ShouldBe(140m);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(100m);

        var stored = (await AllSales(env.Tenant)).Single();
        stored.OnAccountAmount.ShouldBe(140m);
        stored.CurrentAccountId.ShouldBe(customer);

        // Veresiye satış iptali müşteri borcunu geri alır.
        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{stored.Id}/cancel", new { reason = "Hatalı" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        (await CariBalance(env.Tenant, customer)).ShouldBe(0m);
    }

    [Test]
    public async Task Veresiye_RetailCustomer_Returns400() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "VER2", price: 100m);
        await ReceiveStock(env, product, 10m);

        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("onAccount", 120m)], HttpStatusCode.BadRequest)).ShouldBe("veresiye_requires_customer");
        (await AllSales(env.Tenant)).ShouldBeEmpty();
    }

    [Test]
    public async Task Veresiye_OverLimit_RequiresApproval() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product  = await Product(env.Client, "VER3", price: 100m);
        await ReceiveStock(env, product, 10m);
        var customer = await Customer(env, "MUS3", creditLimit: 100m);

        // Limit 100: 120 veresiye aşar; satis.limit-asimi olmadan reddedilir, hiçbir şey yazılmaz.
        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("onAccount", 120m)], HttpStatusCode.Conflict, customer)).ShouldBe("limit_approval_required");
        (await AllSales(env.Tenant)).ShouldBeEmpty();
        (await StockMovements(env.Tenant, "pos-sale")).ShouldBeEmpty();
        (await CariBalance(env.Tenant, customer)).ShouldBe(0m);

        // Limit içi veresiye serbest; limit aşımı onay yetkisi olan oturumla geçer.
        var within = await Sell(env.Client, register, [SaleLine(product, 1m, new { manualDiscountPercent = 0m })], [Pay("cash", 30m), Pay("onAccount", 90m)], customerId: customer);
        within.GetProperty("total").GetDecimal().ShouldBe(120m);

        var approver = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, [..Permissions, "satis.limit-asimi"]);
        var over = await Sell(approver, register, [SaleLine(product)], [Pay("onAccount", 120m)], customerId: customer);
        over.GetProperty("total").GetDecimal().ShouldBe(120m);
        (await CariBalance(env.Tenant, customer)).ShouldBe(210m);
    }
}
