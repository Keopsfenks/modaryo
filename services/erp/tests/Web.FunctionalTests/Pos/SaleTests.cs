using System.Text.Json;
using ClosedXML.Excel;
using ERP.Domain.Catalog;
using ERP.Domain.Pos;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class SaleTests : TestBase {
    internal sealed record Row(string VoucherNo, string Account, decimal Debit, decimal Credit);

    internal static async Task<List<Row>> Journal(Env env) {
        var client = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["muhasebe.export"]);
        var from   = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
        var to     = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd");
        using var response = await client.GetAsync($"/api/erp/accounting/journal.xlsx?from={from}&to={to}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var       sheet    = workbook.Worksheets.First();
        var       rows     = new List<Row>();
        for (var r = 2; r <= (sheet.LastRowUsed()?.RowNumber() ?? 1); r++)
            rows.Add(new Row(sheet.Cell(r, 1).GetString(), sheet.Cell(r, 3).GetString(), sheet.Cell(r, 5).IsEmpty() ? 0m : (decimal)sheet.Cell(r, 5).GetDouble(),
                             sheet.Cell(r, 6).IsEmpty() ? 0m : (decimal)sheet.Cell(r, 6).GetDouble()));
        return rows;
    }

    [Test]
    public async Task FiscalMode_NoInvoice_StockAndCashMoved_YevmiyeHasPosVoucher() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "EKM", price: 100m);   // KDV hariç 100 => 120
        await ReceiveStock(env, product, 10m, cost: 40m);

        var sale = await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 300m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(240m);
        sale.GetProperty("change").GetDecimal().ShouldBe(60m);
        sale.GetProperty("invoiceNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        sale.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        var number = sale.GetProperty("number").GetString()!;
        number.ShouldBe($"PSK1{DateTime.UtcNow.AddHours(3):yyyyMMdd}0001");

        var slip = sale.GetProperty("slip");
        slip.GetProperty("notes")[0].GetString().ShouldBe("Bilgi fişidir. Mali değeri yoktur.");
        slip.GetProperty("barcode").GetString().ShouldBe(number);
        slip.GetProperty("vatSummary")[0].GetProperty("vat").GetDecimal().ShouldBe(40m);

        // Fatura yok; stok çıkışı tek; nakit hesabına net tutar girdi.
        await using (var db = PricingTestSupport.Db(env.Tenant)) (await db.Invoices.CountAsync()).ShouldBe(0);
        var stock = await StockMovements(env.Tenant, "pos-sale");
        stock.Count.ShouldBe(1);
        stock[0].Quantity.ShouldBe(-2m);
        stock[0].Type.ShouldBe(ERP.Domain.Inventory.MovementType.PosSale);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(240m);

        // Birim maliyet satıra yazılır (iade bu maliyetle girer).
        (await AllSales(env.Tenant)).Single().Lines.Single().UnitCost.ShouldBe(40m);

        var journal = await Journal(env);
        var voucher = journal.Where(r => r.VoucherNo == number).Select(r => (r.Account, r.Debit, r.Credit)).ToArray();
        voucher.ShouldBe([("100", 240m, 0m), ("600", 0m, 200m), ("391", 0m, 40m)], ignoreOrder: true);
    }

    [Test]
    public async Task MixedPayment_ChangeComputed_CashNetOfChange() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "KARMA", price: 347.50m, vatIncluded: true);   // KDV dahil 347,50
        await ReceiveStock(env, product, 5m);

        var sale = await Sell(env.Client, register, [SaleLine(product)], [Pay("card", 200m), Pay("cash", 200m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(347.50m);
        sale.GetProperty("change").GetDecimal().ShouldBe(52.50m);

        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(147.50m);
        (await Balance(env.Tenant, env.CardAccount)).ShouldBe(200m);
        var stored = (await AllSales(env.Tenant)).Single();
        stored.CashNet.ShouldBe(147.50m);
        stored.Payments.Single(p => p.Method == PosPaymentMethod.Cash).Amount.ShouldBe(200m);

        var number  = sale.GetProperty("number").GetString()!;
        var voucher = (await Journal(env)).Where(r => r.VoucherNo == number).ToList();
        voucher.Sum(r => r.Debit).ShouldBe(voucher.Sum(r => r.Credit));
        voucher.Single(r => r.Account == "100").Debit.ShouldBe(147.50m);
        voucher.Single(r => r.Account == "102").Debit.ShouldBe(200m);

        // Yetersiz ödeme ve kalanı aşan kart.
        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("cash", 300m)], HttpStatusCode.BadRequest)).ShouldBe("payment_insufficient");
        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("card", 400m)], HttpStatusCode.BadRequest)).ShouldBe("card_exceeds_remaining");
    }

    [Test]
    public async Task CampaignAppliedAutomatically() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "KAMP", price: 100m);
        await ReceiveStock(env, product, 10m);
        using var created = await env.Client.PostAsJsonAsync("/api/erp/pricing/campaigns", new { name = "Yüzde 10", kind = "PercentOff", percent = 10m, productIds = new[] { product } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var sale = await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 216m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(216m);   // 240 - %10
        sale.GetProperty("slip").GetProperty("lines")[0].GetProperty("campaignDiscount").GetDecimal().ShouldBe(24m);
        var line = (await AllSales(env.Tenant)).Single().Lines.Single();
        line.CampaignDiscount.ShouldBe(20m);
        line.CampaignId.ShouldNotBeNull();
        line.Net.ShouldBe(180m);
    }

    [Test]
    public async Task ManualDiscountOverLimit_Returns403() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "ISK", price: 100m);
        await ReceiveStock(env, product, 10m);
        var limited = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, 5m, Permissions);

        (await SellFails(limited, register, [SaleLine(product, 1m, new { manualDiscountPercent = 10m })], [Pay("cash", 120m)], HttpStatusCode.Forbidden)).ShouldBe("discount_limit_exceeded");
        (await StockMovements(env.Tenant, "pos-sale")).ShouldBeEmpty();

        var allowed = await Sell(limited, register, [SaleLine(product, 1m, new { manualDiscountPercent = 5m })], [Pay("cash", 114m)]);
        allowed.GetProperty("total").GetDecimal().ShouldBe(114m);
    }

    [Test]
    public async Task NoOpenShift_Returns409() {
        var env      = await NewEnv();
        var register = (await CreateRegister(env)).GetProperty("id").GetGuid();
        var product  = await Product(env.Client, "VRD", price: 10m);
        await ReceiveStock(env, product, 10m);

        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("cash", 12m)], HttpStatusCode.Conflict)).ShouldBe("shift_required");

        // Başka kasiyerin vardiyası sayılmaz.
        await OpenShiftDb(env, register, 0m, Guid.NewGuid());
        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("cash", 12m)], HttpStatusCode.Conflict)).ShouldBe("shift_required");
    }

    [Test]
    public async Task CancelSameShift_Reverses() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "IPT", price: 100m);
        await ReceiveStock(env, product, 10m);
        var sale = await Sell(env.Client, register, [SaleLine(product, 3m)], [Pay("cash", 360m)]);
        var id   = sale.GetProperty("id").GetGuid();

        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{id}/cancel", new { reason = "Müşteri vazgeçti" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        (await cancel.JsonAsync()).GetProperty("status").GetString().ShouldBe("cancelled");

        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(0m);
        var stock = await StockMovements(env.Tenant, "pos-sale");
        stock.Sum(m => m.Quantity).ShouldBe(0m);   // çıkış + tek ters hareket

        using var again = await env.Client.PostAsJsonAsync($"{Api}/sales/{id}/cancel", new { reason = "Yine" });
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StockMovements(env.Tenant, "pos-sale")).Count.ShouldBe(stock.Count);

        var number  = sale.GetProperty("number").GetString()!;
        var journal = await Journal(env);
        journal.Where(r => r.VoucherNo == number).Sum(r => r.Debit).ShouldBe(360m);
        journal.Where(r => r.VoucherNo == number + "-IPT").Sum(r => r.Credit).ShouldBe(360m);

        // İptal edilen satışın numarası kullanılmış kalır.
        var next = await Sell(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)]);
        next.GetProperty("number").GetString().ShouldEndWith("0002");
    }

    [Test]
    public async Task CancelOtherDay_Returns409() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "ESK", price: 100m);
        await ReceiveStock(env, product, 10m);
        var sale = await Sell(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)]);
        var id   = sale.GetProperty("id").GetGuid();

        await using (var db = PricingTestSupport.Db(env.Tenant))
            await db.PosSales.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Date, x => x.Date.AddDays(-1)));

        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{id}/cancel", new { reason = "Dünkü satış" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict, await cancel.Content.ReadAsStringAsync());
        (await cancel.ErrorCodeAsync()).ShouldBe("cancel_not_allowed");
    }

    [Test]
    public async Task ScalePriceLine_TotalEqualsEmbeddedPrice() {
        var env = await NewEnv();
        await SetScaleFormats(env.Client);
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "12345", new { baseUnit = "kg" }, price: 80m);   // KDV dahil 96
        await ReceiveStock(env, product, 10m);

        // 40 / 96 = 0,417 kg: miktar x fiyat 40,03 olurdu; satır toplamı barkoddaki 40,00'dır.
        var hit  = await Lookup(env.Client, Ean13.Build("2812345", 4000));
        var sale = await Sell(env.Client, register, [SaleLine(product, hit.GetProperty("quantity").GetDecimal(), new { unit = "kg", fixedTotal = hit.GetProperty("fixedTotal").GetDecimal() })], [Pay("cash", 40m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(40m);
        var line = (await AllSales(env.Tenant)).Single().Lines.Single();
        line.Total.ShouldBe(40m);
        (line.Net + line.Vat).ShouldBe(40m);

        // Tutarsız fixedTotal reddedilir.
        (await SellFails(env.Client, register, [SaleLine(product, 0.417m, new { fixedTotal = 10m })], [Pay("cash", 10m)], HttpStatusCode.BadRequest)).ShouldBe("fixed_total_invalid");
    }

    [Test]
    public async Task DoubleSubmit_SameClientSaleId_WritesOneSale() {
        var env = await NewEnv();
        var (register, shift) = await RegisterWithShift(env);
        var product = await Product(env.Client, "TEK", price: 100m);
        await ReceiveStock(env, product, 10m);
        var id = Guid.NewGuid();

        // Eşzamanlı çift gönderim: tek satış, tek stok çıkışı, tek hesap hareketi.
        var both = await Task.WhenAll(PostSale(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], id), PostSale(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], id));
        try {
            foreach (var response in both) response.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
            both.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            var bodies = await Task.WhenAll(both.Select(r => r.JsonAsync()));
            bodies[0].GetProperty("id").GetGuid().ShouldBe(bodies[1].GetProperty("id").GetGuid());
            bodies[0].GetProperty("number").GetString().ShouldBe(bodies[1].GetProperty("number").GetString());
        } finally {
            foreach (var response in both) response.Dispose();
        }

        // Sıralı yeniden gönderim: kapanmış vardiyada bile kayıtlı satış döner.
        await using (var db = PricingTestSupport.Db(env.Tenant))
            await db.PosShifts.Where(s => s.Id == shift).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ShiftStatus.Closed));
        using var retry = await PostSale(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], id);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        (await retry.JsonAsync()).GetProperty("replayed").GetBoolean().ShouldBeTrue();

        (await AllSales(env.Tenant)).Count.ShouldBe(1);
        (await StockMovements(env.Tenant, "pos-sale")).Count.ShouldBe(1);
        (await Movements(env.Tenant, env.CashAccount)).Count.ShouldBe(1);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(120m);
    }

    [Test]
    public async Task ReplayDoesNotAdvanceReceiptSequence() {
        var env = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "SIRA", price: 100m);
        await ReceiveStock(env, product, 10m);
        var id = Guid.NewGuid();

        var first = await Sell(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], id);
        using (var replay = await PostSale(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], id)) replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await Sell(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)]);

        first.GetProperty("number").GetString()!.ShouldEndWith("0001");
        second.GetProperty("number").GetString()!.ShouldEndWith("0002");
    }

    [Test]
    public async Task ConcurrentPosts_LastUnit_OneSucceeds() {
        var env = await NewEnv();
        // İki kasa aynı depoyu paylaşır; her birinin kendi açık vardiyası vardır (vardiya kilidi iki satışı sıraya sokmaz, stok kilidi sokar).
        var (first, _)  = await RegisterWithShift(env, "K1");
        var (second, _) = await RegisterWithShift(env, "K2");
        var product     = await Product(env.Client, "SON", price: 100m);
        await ReceiveStock(env, product, 1m);

        var responses = await Task.WhenAll(PostSale(env.Client, first, [SaleLine(product)], [Pay("cash", 120m)]), PostSale(env.Client, second, [SaleLine(product)], [Pay("cash", 120m)]));
        try {
            responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1, string.Join(" | ", await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()))));
            var failed = responses.Single(r => r.StatusCode != HttpStatusCode.Created);
            failed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await failed.ErrorCodeAsync()).ShouldBe("insufficient_stock");
        } finally {
            foreach (var response in responses) response.Dispose();
        }

        (await AllSales(env.Tenant)).Count.ShouldBe(1);
        (await StockMovements(env.Tenant, "pos-sale")).Count.ShouldBe(1);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(120m);
    }
}
