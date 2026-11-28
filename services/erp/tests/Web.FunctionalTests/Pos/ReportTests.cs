using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class ReportTests : TestBase {
    private static string Today => DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd");

    [Test]
    public async Task DayReport_TotalsByMethodVatAndReceiptRange() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register);
        var vat20 = await Product(env.Client, "R20", price: 100m);
        var vat10 = await Product(env.Client, "R10", new { vatRate = 10 }, price: 100m);
        await ReceiveStock(env, vat20, 10m);
        await ReceiveStock(env, vat10, 10m);

        var first  = await Sell(env.Client, register, [SaleLine(vat20, 2m)], [Pay("cash", 300m)]);                              // 240, para üstü 60
        var second = await Sell(env.Client, register, [SaleLine(vat10)], [Pay("card", 110m)]);                                    // 110
        var third  = await Sell(env.Client, register, [SaleLine(vat20)], [Pay("cash", 120m)]);                                    // iptal
        using (var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{third.GetProperty("id").GetGuid()}/cancel", new { reason = "Hata" })) cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        var number = first.GetProperty("number").GetString()!;
        await Return(env.Client, number, await SaleLineId(env, number), 1m, refund: [new { method = "cash", amount = 120m }]);

        using var response = await env.Client.GetAsync($"{Api}/reports/day?registerId={register}&date={Today}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await response.JsonAsync();
        report.GetProperty("salesCount").GetInt32().ShouldBe(2);
        report.GetProperty("salesTotal").GetDecimal().ShouldBe(350m);
        report.GetProperty("cashNet").GetDecimal().ShouldBe(240m);
        report.GetProperty("card").GetDecimal().ShouldBe(110m);
        report.GetProperty("changeGiven").GetDecimal().ShouldBe(60m);
        report.GetProperty("cancelledCount").GetInt32().ShouldBe(1);
        report.GetProperty("returnsTotal").GetDecimal().ShouldBe(120m);
        report.GetProperty("cashRefunds").GetDecimal().ShouldBe(120m);
        report.GetProperty("netSales").GetDecimal().ShouldBe(230m);

        var vat = report.GetProperty("vatSummary").EnumerateArray().ToDictionary(v => v.GetProperty("rate").GetInt32(), v => (v.GetProperty("base").GetDecimal(), v.GetProperty("vat").GetDecimal()));
        vat[20].ShouldBe((200m, 40m));
        vat[10].ShouldBe((100m, 10m));
        report.GetProperty("returnVatSummary")[0].GetProperty("vat").GetDecimal().ShouldBe(20m);

        report.GetProperty("firstReceipt").GetString().ShouldBe(number);
        report.GetProperty("lastReceipt").GetString().ShouldBe(third.GetProperty("number").GetString());
        report.GetProperty("cashiers")[0].GetProperty("salesCount").GetInt32().ShouldBe(2);

        // Yetkisiz kullanıcı gün sonu raporunu göremez.
        var cashier = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["pos.sell"]);
        using var denied = await cashier.GetAsync($"{Api}/reports/day?registerId={register}&date={Today}");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task CashierReport() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        await OpenShiftApi(env, register);
        var product = await Product(env.Client, "KSR", price: 100m);
        await ReceiveStock(env, product, 10m);
        await Sell(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)]);
        await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 240m)]);

        using var response = await env.Client.GetAsync($"{Api}/reports/cashiers?from={Today}&to={Today}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rows = await response.JsonAsync();
        rows.GetArrayLength().ShouldBe(1);
        rows[0].GetProperty("cashierUserId").GetGuid().ShouldBe(SessionUser);
        rows[0].GetProperty("salesCount").GetInt32().ShouldBe(2);
        rows[0].GetProperty("salesTotal").GetDecimal().ShouldBe(360m);

        using var tooLong = await env.Client.GetAsync($"{Api}/reports/cashiers?from=2020-01-01&to={Today}");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
