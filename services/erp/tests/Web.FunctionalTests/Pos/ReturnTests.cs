using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class ReturnTests : TestBase {
    [Test]
    public async Task Return_ExceedsSold_Returns400() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register);
        var product = await Product(env.Client, "IAD", price: 100m);
        await ReceiveStock(env, product, 10m, cost: 40m);
        var number = (await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 240m)])).GetProperty("number").GetString()!;
        var line   = await SaleLineId(env, number);

        using var tooMany = await PostReturn(env.Client, number, line, 3m, [new { method = "cash", amount = 360m }]);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await tooMany.Content.ReadAsStringAsync());
        (await tooMany.ErrorCodeAsync()).ShouldBe("return_exceeds_sold");

        // Yanlış dağılım reddedilir; doğrusu stoğu satış maliyetiyle geri alır.
        using var mismatch = await PostReturn(env.Client, number, line, 1m, [new { method = "cash", amount = 100m }]);
        (await mismatch.ErrorCodeAsync()).ShouldBe("refund_mismatch");

        var id  = Guid.NewGuid();
        var one = await Return(env.Client, number, line, 1m, [new { method = "card", amount = 120m }], clientReturnId: id);
        one.GetProperty("cardRefund").GetDecimal().ShouldBe(120m);
        (await Balance(env.Tenant, env.CardAccount)).ShouldBe(-120m);

        // Aynı kimlikle yeniden gönderim yeni iade yazmaz.
        var replay = await Return(env.Client, number, line, 1m, [new { method = "card", amount = 120m }], clientReturnId: id);
        replay.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        replay.GetProperty("id").GetGuid().ShouldBe(one.GetProperty("id").GetGuid());

        var received = (await StockMovements(env.Tenant, "pos-return")).Single();
        received.Type.ShouldBe(MovementType.PosReturn);
        received.Quantity.ShouldBe(1m);
        received.UnitCost.ShouldBe(40m);

        // Kalan 1 adet iade edilir, sonra fazlası reddedilir; iadesi olan satış iptal edilemez.
        await Return(env.Client, number, line, 1m, [new { method = "cash", amount = 120m }]);
        using var more = await PostReturn(env.Client, number, line, 1m, [new { method = "cash", amount = 120m }]);
        (await more.ErrorCodeAsync()).ShouldBe("return_exceeds_sold");
        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{(await AllSales(env.Tenant)).Single().Id}/cancel", new { reason = "Deneme" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await cancel.ErrorCodeAsync()).ShouldBe("sale_has_returns");

        // İade fişi dengeli: B 610 + B 391 / A 102 + A 100.
        var journal = (await SaleTests.Journal(env)).Where(r => r.VoucherNo.StartsWith("PIK1")).ToList();
        journal.Sum(r => r.Debit).ShouldBe(240m);
        journal.Sum(r => r.Credit).ShouldBe(240m);
        journal.Where(r => r.Account == "610").Sum(r => r.Debit).ShouldBe(200m);
    }

    [Test]
    public async Task Exchange_DifferenceSettled() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register);
        var a = await Product(env.Client, "XA", price: 100m);
        var b = await Product(env.Client, "XB", price: 200m);   // 240
        await ReceiveStock(env, a, 10m);
        await ReceiveStock(env, b, 10m);
        var number = (await Sell(env.Client, register, [SaleLine(a)], [Pay("card", 120m)])).GetProperty("number").GetString()!;
        var line   = await SaleLineId(env, number);

        // Fark 120 ödenmeli: eksik ödeme ve iade dağılımı birlikte reddedilir.
        using var short1 = await PostReturn(env.Client, number, line, 1m, exchangeLines: [SaleLine(b)], exchangePayments: [Pay("cash", 100m)]);
        short1.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await short1.Content.ReadAsStringAsync());
        (await short1.ErrorCodeAsync()).ShouldBe("payment_insufficient");
        using var both = await PostReturn(env.Client, number, line, 1m, [new { method = "cash", amount = 10m }], [SaleLine(b)], [Pay("cash", 120m)]);
        (await both.ErrorCodeAsync()).ShouldBe("refund_mismatch");
        (await AllSales(env.Tenant)).Count.ShouldBe(1);   // geri alındı

        var done = await Return(env.Client, number, line, 1m, exchangeLines: [SaleLine(b)], exchangePayments: [Pay("card", 120m)]);
        done.GetProperty("exchangeCredit").GetDecimal().ShouldBe(120m);
        done.GetProperty("difference").GetDecimal().ShouldBe(120m);
        (await Balance(env.Tenant, env.CardAccount)).ShouldBe(240m);   // 120 satış + 120 fark
        (await AllSales(env.Tenant)).Count.ShouldBe(2);

        // Stok: A geri girdi, B çıktı.
        (await StockMovements(env.Tenant, "pos-return")).Single().Quantity.ShouldBe(1m);
        var journal = await SaleTests.Journal(env);
        foreach (var voucher in journal.GroupBy(r => r.VoucherNo)) voucher.Sum(r => r.Debit).ShouldBe(voucher.Sum(r => r.Credit));
    }

    [Test]
    public async Task EArchiveReturn_CreatesDraftReturnInvoiceAndPayout() {
        var env      = await EArchiveModeTests.NewEArchiveEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register);
        var product = await Product(env.Client, "EIA", price: 100m);
        await ReceiveStock(env, product, 10m);
        var number = (await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 240m)])).GetProperty("number").GetString()!;
        var line   = await SaleLineId(env, number);

        var result = await Return(env.Client, number, line, 1m, [new { method = "cash", amount = 120m }]);
        result.GetProperty("returnInvoiceId").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);

        await using var db    = PricingTestSupport.Db(env.Tenant);
        var             draft = await db.Invoices.AsNoTracking().SingleAsync(i => i.Kind == InvoiceKind.SalesReturn);
        draft.Status.ShouldBe(InvoiceStatus.Draft);
        draft.AffectsStock.ShouldBeFalse();
        draft.SourceType.ShouldBe("pos-return");
        draft.Payable.ShouldBe(120m);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(120m);   // 240 giriş - 120 iade
        (await StockMovements(env.Tenant, "pos-return")).Count.ShouldBe(1);
    }
}
