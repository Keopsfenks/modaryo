using ERP.Domain.CurrentAccounts;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

/// <summary>P9 son inceleme düzeltmeleri (C1, C2, I1-I4).</summary>
public class FinalReviewFixTests : TestBase {
    private static async Task<HttpResponseMessage> Cancel(HttpClient client, Guid saleId) {
        return await client.PostAsJsonAsync($"{Api}/sales/{saleId}/cancel", new { reason = "Hatalı" });
    }

    // ---- C1 ---------------------------------------------------------------------------------------------------------

    [Test]
    public async Task ConcurrentReturns_OnTwoRegisters_OnlyOneIsRefunded() {
        var env     = await NewEnv();
        var (k1, _) = await RegisterWithShift(env, "K1");
        var (k2, _) = await RegisterWithShift(env, "K2");
        var product = await Product(env.Client, "C1A", price: 100m);
        await ReceiveStock(env, product, 50m);

        for (var round = 0; round < 6; round++) {
            var number = (await Sell(env.Client, k1, [SaleLine(product)], [Pay("cash", 120m)])).GetProperty("number").GetString()!;
            var line   = await SaleLineId(env, number);
            var refund = new object[] { new { method = "cash", amount = 120m } };
            var both   = await Task.WhenAll(PostReturn(env.Client, number, line, 1m, refund, registerId: k1), PostReturn(env.Client, number, line, 1m, refund, registerId: k2));
            try {
                both.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1, $"tur {round}: " + string.Join(" | ", await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()))));
                var failed = both.Single(r => r.StatusCode != HttpStatusCode.OK);
                failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                (await failed.ErrorCodeAsync()).ShouldBe("return_exceeds_sold");
            } finally {
                foreach (var response in both) response.Dispose();
            }
        }

        (await StockMovements(env.Tenant, "pos-return")).Count.ShouldBe(6);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(0m);   // 6 x (+120 satış - 120 tek iade)
    }

    [Test]
    public async Task ConcurrentReturnAndCancel_OnTwoRegisters_OnlyOneWins() {
        var env     = await NewEnv();
        var (k1, _) = await RegisterWithShift(env, "K1");
        var (k2, _) = await RegisterWithShift(env, "K2");
        var product = await Product(env.Client, "C1B", price: 100m);
        await ReceiveStock(env, product, 50m);

        for (var round = 0; round < 6; round++) {
            var sale   = await Sell(env.Client, k1, [SaleLine(product)], [Pay("cash", 120m)]);
            var number = sale.GetProperty("number").GetString()!;
            var line   = await SaleLineId(env, number);
            var refund = new object[] { new { method = "cash", amount = 120m } };
            var both   = await Task.WhenAll(Cancel(env.Client, sale.GetProperty("id").GetGuid()), PostReturn(env.Client, number, line, 1m, refund, registerId: k2));
            try {
                both.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1, $"tur {round}: " + string.Join(" | ", await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()))));
                both.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.ShouldBe(HttpStatusCode.Conflict);
            } finally {
                foreach (var response in both) response.Dispose();
            }
        }

        // Her fiş ya iptal edildi ya iade edildi; ikisi birden olmadı: net nakit 0.
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(0m);
    }

    // ---- C2 ---------------------------------------------------------------------------------------------------------

    [TestCase(1)]
    [TestCase(3)]
    public async Task VatIncludedShelfPrice_ExactPaymentIsAccepted(int quantity) {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product       = await Product(env.Client, "C2A", price: 9.99m, vatIncluded: true);
        await ReceiveStock(env, product, 10m);

        var shelf = 9.99m * quantity;
        var sale  = await Sell(env.Client, register, [SaleLine(product, quantity)], [Pay("card", shelf)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(shelf);
        sale.GetProperty("change").GetDecimal().ShouldBe(0m);
        var stored = (await AllSales(env.Tenant)).Single();
        (stored.Subtotal + stored.VatTotal).ShouldBe(shelf);
    }

    [Test]
    public async Task VatIncludedShelfPrice_EArchiveInvoiceMatchesShelfPrice() {
        var env           = await EArchiveModeTests.NewEArchiveEnv();
        var (register, _) = await RegisterWithShift(env);
        var product       = await Product(env.Client, "C2B", price: 9.99m, vatIncluded: true);
        await ReceiveStock(env, product, 10m);

        var sale = await Sell(env.Client, register, [SaleLine(product, 1m)], [Pay("cash", 9.99m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(9.99m);
        await using var db      = PricingTestSupport.Db(env.Tenant);
        var             invoice = await db.Invoices.AsNoTracking().SingleAsync();
        invoice.Payable.ShouldBe(9.99m);
        invoice.Net.ShouldBe(8.32m);
        invoice.Vat.ShouldBe(1.67m);
    }

    // ---- I1 ---------------------------------------------------------------------------------------------------------

    [Test]
    public async Task SalePreview_MatchesRealSale_AndWritesNothing() {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product       = await Product(env.Client, "PRV1", price: 9.99m, vatIncluded: true);
        await ReceiveStock(env, product, 10m);

        var       lines   = new[] { SaleLine(product, 3m, new { manualDiscountPercent = 5m }) };
        using var preview = await env.Client.PostAsJsonAsync($"{Api}/sales/preview", new { registerId = register, lines });
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var body  = await preview.JsonAsync();
        var total = body.GetProperty("total").GetDecimal();
        body.GetProperty("lines").GetArrayLength().ShouldBe(1);
        var line = body.GetProperty("lines")[0];
        line.GetProperty("total").GetDecimal().ShouldBe(total);
        (line.GetProperty("net").GetDecimal() + line.GetProperty("vat").GetDecimal()).ShouldBe(total);
        body.GetProperty("discount").GetDecimal().ShouldBeGreaterThan(0m);

        (await AllSales(env.Tenant)).ShouldBeEmpty();
        (await StockMovements(env.Tenant, "pos-sale")).ShouldBeEmpty();
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(0m);

        var sale = await Sell(env.Client, register, lines, [Pay("cash", total)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(total);
    }

    [Test]
    public async Task SalePreview_RequiresSellPermission_AndValidatesLines() {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var reader        = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["pos.reports"]);
        using var denied  = await reader.PostAsJsonAsync($"{Api}/sales/preview", new { registerId = register, lines = Array.Empty<object>() });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var empty = await env.Client.PostAsJsonAsync($"{Api}/sales/preview", new { registerId = register, lines = Array.Empty<object>() });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ReturnPreview_MatchesRealReturn_AndWritesNothing() {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var a             = await Product(env.Client, "RPA", price: 100m);
        var b             = await Product(env.Client, "RPB", price: 200m);   // 240
        await ReceiveStock(env, a, 10m);
        await ReceiveStock(env, b, 10m);
        var number = (await Sell(env.Client, register, [SaleLine(a, 2m)], [Pay("cash", 240m)])).GetProperty("number").GetString()!;
        var line   = await SaleLineId(env, number);

        using var plain = await env.Client.PostAsJsonAsync($"{Api}/returns/preview", new { saleNumber = number, registerId = register, lines = new[] { new { saleLineId = line, quantity = 1m } } });
        plain.StatusCode.ShouldBe(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());
        var p = await plain.JsonAsync();
        p.GetProperty("returnValue").GetDecimal().ShouldBe(120m);
        p.GetProperty("refundDue").GetDecimal().ShouldBe(120m);
        p.GetProperty("payDue").GetDecimal().ShouldBe(0m);
        p.GetProperty("exchangeCredit").GetDecimal().ShouldBe(0m);

        using var swap = await env.Client.PostAsJsonAsync($"{Api}/returns/preview", new {
            saleNumber = number, registerId = register, lines = new[] { new { saleLineId = line, quantity = 1m } }, exchangeLines = new[] { SaleLine(b) }
        });
        swap.StatusCode.ShouldBe(HttpStatusCode.OK, await swap.Content.ReadAsStringAsync());
        var s = await swap.JsonAsync();
        s.GetProperty("exchangeCredit").GetDecimal().ShouldBe(120m);
        s.GetProperty("payDue").GetDecimal().ShouldBe(120m);
        s.GetProperty("refundDue").GetDecimal().ShouldBe(0m);
        s.GetProperty("exchange").GetProperty("total").GetDecimal().ShouldBe(240m);

        // Fazla miktar önizlemede de reddedilir.
        using var tooMany = await env.Client.PostAsJsonAsync($"{Api}/returns/preview", new { saleNumber = number, lines = new[] { new { saleLineId = line, quantity = 3m } } });
        (await tooMany.ErrorCodeAsync()).ShouldBe("return_exceeds_sold");

        // Hiçbir şey yazılmadı: iade ve değişim satışı yok, iade edilen miktar 0.
        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            (await db.PosReturns.CountAsync()).ShouldBe(0);
            (await db.PosSaleLines.AsNoTracking().SingleAsync()).ReturnedQuantity.ShouldBe(0m);
        }

        (await AllSales(env.Tenant)).Count.ShouldBe(1);

        var real = await Return(env.Client, number, line, 1m, exchangeLines: [SaleLine(b)], exchangePayments: [Pay("cash", 120m)]);
        real.GetProperty("returnValue").GetDecimal().ShouldBe(120m);
        real.GetProperty("exchangeCredit").GetDecimal().ShouldBe(120m);
    }

    // ---- I2 ---------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Cancel_ExchangeSale_IsRejected() {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var a             = await Product(env.Client, "I2A", price: 100m);
        var b             = await Product(env.Client, "I2B", price: 200m);
        await ReceiveStock(env, a, 10m);
        await ReceiveStock(env, b, 10m);
        var number = (await Sell(env.Client, register, [SaleLine(a)], [Pay("card", 120m)])).GetProperty("number").GetString()!;
        var line   = await SaleLineId(env, number);

        var done       = await Return(env.Client, number, line, 1m, exchangeLines: [SaleLine(b)], exchangePayments: [Pay("cash", 120m)]);
        var exchangeId = done.GetProperty("exchangeSale").GetProperty("id").GetGuid();

        using var cancel = await Cancel(env.Client, exchangeId);
        cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict, await cancel.Content.ReadAsStringAsync());
        (await cancel.ErrorCodeAsync()).ShouldBe("cancel_not_allowed");

        var sales = await AllSales(env.Tenant);
        sales.ShouldAllBe(s => s.Status == ERP.Domain.Pos.PosSaleStatus.Completed);
        (await StockMovements(env.Tenant, "pos-sale")).Count(m => m.Quantity < 0m).ShouldBe(2);
    }

    // ---- I3 ---------------------------------------------------------------------------------------------------------

    [Test]
    public async Task EArchive_RetailSale_DoesNotWaitForRetailCariLock() {
        var env           = await EArchiveModeTests.NewEArchiveEnv();
        var (register, _) = await RegisterWithShift(env);
        var product       = await Product(env.Client, "I3A", price: 100m);
        await ReceiveStock(env, product, 10m);
        await GetSettings(env.Client);   // perakende cari hazırlanır

        // Perakende cari satırını başka bir işlemde kilitli tut: kilide bağlı bir satış burada takılır.
        await using var holder = PricingTestSupport.Db(env.Tenant);
        var             retail = await holder.CurrentAccounts.AsNoTracking().Where(c => c.Code == "PERAKENDE").Select(c => c.Id).SingleAsync();
        await using var tx     = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlAsync($"SELECT \"Id\" FROM current_accounts WHERE \"Id\" = {retail} FOR NO KEY UPDATE");
        try {
            var sale = PostSale(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)]);
            var done = await Task.WhenAny(sale, Task.Delay(TimeSpan.FromSeconds(10)));
            (done == sale).ShouldBeTrue("Perakende e-Arşiv satışı perakende cari kilidini beklememeli.");
            using var response = await sale;
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        } finally {
            await tx.RollbackAsync();
        }
    }

    [Test]
    public async Task EArchive_ParallelRetailSales_AllCloseTheirInvoices_AndCancelWorks() {
        var env       = await EArchiveModeTests.NewEArchiveEnv();
        var registers = new List<Guid>();
        foreach (var code in new[] { "K1", "K2", "K3" }) registers.Add((await RegisterWithShift(env, code)).Register);
        var product = await Product(env.Client, "I3B", price: 100m);
        await ReceiveStock(env, product, 50m);

        var posts = Enumerable.Range(0, 9).Select(i => PostSale(env.Client, registers[i % 3], [SaleLine(product)], [Pay("cash", 120m)])).ToArray();
        var all   = await Task.WhenAll(posts);
        try {
            foreach (var r in all) r.StatusCode.ShouldBe(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            var first = await all[0].JsonAsync();

            // Her fatura vade satırı tahsilatla tam eşleşmiştir (artımlı eşleştirme; yeniden kurma yok).
            await using (var db = PricingTestSupport.Db(env.Tenant)) {
                var retail = await db.CurrentAccounts.AsNoTracking().Where(c => c.Code == "PERAKENDE").Select(c => c.Id).SingleAsync();
                var dues   = await db.LedgerDueLines.AsNoTracking().Where(d => d.CurrentAccountId == retail && d.Direction == DueDirection.Receivable).ToListAsync();
                var allocs = await db.LedgerAllocations.AsNoTracking().Where(a => a.CurrentAccountId == retail).ToListAsync();
                dues.Count.ShouldBe(9);
                foreach (var due in dues) allocs.Where(a => a.DebitLineId == due.Id).Sum(a => a.Amount).ShouldBe(due.Amount);
            }

            using var cancel = await Cancel(env.Client, first.GetProperty("id").GetGuid());
            cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
            (await Balance(env.Tenant, env.CashAccount)).ShouldBe(8 * 120m);
        } finally {
            foreach (var response in all) response.Dispose();
        }
    }

    // ---- I4 ---------------------------------------------------------------------------------------------------------

    [Test]
    public async Task UnpricedItem_ClientPrice_RequiresPriceOverridePermission() {
        var env           = await NewEnv();
        var (register, _) = await RegisterWithShift(env);
        var product       = await Product(env.Client, "I4A", extra: new { defaultSalePrice = (object?)null });
        await ReceiveStock(env, product, 10m);
        var cashier = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, Permissions.Where(p => p != "pos.manage").ToArray());

        foreach (var price in new[] { 50m, 0m }) {
            using var denied = await PostSale(cashier, register, [SaleLine(product, 1m, new { unitPrice = price })], [Pay("cash", 50m)]);
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await denied.Content.ReadAsStringAsync());
            (await denied.ErrorCodeAsync()).ShouldBe("price_override_required");
        }

        using var negative = await PostSale(env.Client, register, [SaleLine(product, 1m, new { unitPrice = -5m })], [Pay("cash", 50m)]);
        negative.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AllSales(env.Tenant)).ShouldBeEmpty();

        // Fiyat yetkisi olan (pos.manage) kasiyer açık fiyat girebilir.
        var ok = await Sell(env.Client, register, [SaleLine(product, 1m, new { unitPrice = 50m })], [Pay("cash", 50m)]);
        ok.GetProperty("total").GetDecimal().ShouldBe(50m);
    }
}
