using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class ShiftTests : TestBase {
    [Test]
    public async Task OpenSecondShiftSameRegister_Returns409() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register, 100m);

        using var second = await env.Client.PostAsJsonAsync($"{Api}/shifts/open", new { registerId = register, openingCash = 0m });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict, await second.Content.ReadAsStringAsync());
        (await second.ErrorCodeAsync()).ShouldBe("shift_already_open");

        // Başka kasiyer de aynı kasada ikinci vardiya açamaz.
        var other = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, Permissions);
        using var third = await other.PostAsJsonAsync($"{Api}/shifts/open", new { registerId = register, openingCash = 0m });
        third.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var current = await env.Client.GetAsync($"{Api}/shifts/current?registerId={register}");
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task ShiftClose_ExpectedAfterExchangeAndRefund() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        var shift    = await OpenShiftApi(env, register, 100m);
        var a        = await Product(env.Client, "ADI", price: 100m);   // 120 KDV dahil
        var b        = await Product(env.Client, "BDI", price: 50m);    // 60 KDV dahil
        await ReceiveStock(env, a, 10m, cost: 40m);
        await ReceiveStock(env, b, 10m, cost: 20m);

        // Satış: 2 x A = 240 nakit (300 verildi, 60 para üstü) => kasaya +240.
        var sale = await Sell(env.Client, register, [SaleLine(a, 2m)], [Pay("cash", 300m)]);
        var number = sale.GetProperty("number").GetString()!;

        // Değişim 1: 1 x A (120) iade, 1 x B (60) alınır: 60 mahsup, 60 nakit iade => kasadan -60.
        var line = await SaleLineId(env, number);
        var one  = await Return(env.Client, number, line, 1m, refund: [new { method = "cash", amount = 60m }], exchangeLines: [SaleLine(b)]);
        one.GetProperty("returnValue").GetDecimal().ShouldBe(120m);
        one.GetProperty("exchangeCredit").GetDecimal().ShouldBe(60m);
        one.GetProperty("cashRefund").GetDecimal().ShouldBe(60m);
        one.GetProperty("exchangeSale").GetProperty("total").GetDecimal().ShouldBe(60m);

        // Satış 2: 1 x B = 60 nakit. Değişim 2: B iade (60), A (120) alınır: 60 mahsup, 100 verilir, 60 net, 40 para üstü => kasaya +60.
        var second = await Sell(env.Client, register, [SaleLine(b)], [Pay("cash", 60m)]);
        var secondNumber = second.GetProperty("number").GetString()!;
        var two = await Return(env.Client, secondNumber, await SaleLineId(env, secondNumber), 1m, exchangeLines: [SaleLine(a)], exchangePayments: [Pay("cash", 100m)]);
        two.GetProperty("exchangeSale").GetProperty("change").GetDecimal().ShouldBe(40m);
        two.GetProperty("exchangeSale").GetProperty("total").GetDecimal().ShouldBe(120m);

        // Beklenen: 100 + 240 - 60 + 60 + 60 = 400.
        var report = await CloseShift(env.Client, shift, 390m);
        var totals = report.GetProperty("totals");
        totals.GetProperty("expectedCash").GetDecimal().ShouldBe(400m);
        totals.GetProperty("cashRefunds").GetDecimal().ShouldBe(60m);
        report.GetProperty("shift").GetProperty("difference").GetDecimal().ShouldBe(-10m);
        report.GetProperty("shift").GetProperty("status").GetString().ShouldBe("closed");

        // Kapanan vardiyada satış yapılamaz; ikinci kapanış 409.
        (await SellFails(env.Client, register, [SaleLine(a)], [Pay("cash", 120m)], HttpStatusCode.Conflict)).ShouldBe("shift_required");
        using var again = await env.Client.PostAsJsonAsync($"{Api}/shifts/{shift}/close", new { countedCash = 0m });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task CashOut_ReducesExpected() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        var shift    = await OpenShiftApi(env, register, 50m);

        using var cashIn = await env.Client.PostAsJsonAsync($"{Api}/shifts/{shift}/cash-moves", new { kind = "in", amount = 20m, reason = "Bozuk para" });
        cashIn.StatusCode.ShouldBe(HttpStatusCode.OK, await cashIn.Content.ReadAsStringAsync());
        using var cashOut = await env.Client.PostAsJsonAsync($"{Api}/shifts/{shift}/cash-moves", new { kind = "out", amount = 30m, reason = "Avans" });
        (await cashOut.JsonAsync()).GetProperty("expectedCash").GetDecimal().ShouldBe(40m);

        // Eksiye düşen çıkış uyarı verir (varsayılan ayar).
        using var over = await env.Client.PostAsJsonAsync($"{Api}/shifts/{shift}/cash-moves", new { kind = "out", amount = 100m, reason = "Fazla" });
        over.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await over.JsonAsync()).GetProperty("warnings")[0].GetString().ShouldBe("cash_negative");

        var report = await CloseShift(env.Client, shift, -60m + 60m);
        report.GetProperty("totals").GetProperty("expectedCash").GetDecimal().ShouldBe(-60m);
        report.GetProperty("totals").GetProperty("cashOut").GetDecimal().ShouldBe(130m);

        using var late = await env.Client.PostAsJsonAsync($"{Api}/shifts/{shift}/cash-moves", new { kind = "in", amount = 1m, reason = "Geç" });
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
