using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class EArchiveModeTests : TestBase {
    internal static async Task<Env> NewEArchiveEnv() {
        var env = await NewEnv();
        await Invoicing.InvoiceTestSupport.EnableEBelge(env.Client);
        var admin = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["ayarlar.belgeler", "fatura.read"]);
        using var series = await admin.PostAsJsonAsync("/api/erp/document-series", new { branchId = env.Branch, kindGroup = "sales", mode = "EArchive", prefix = "EAR", year = DateTime.UtcNow.Year, isDefault = true });
        series.StatusCode.ShouldBe(HttpStatusCode.Created, await series.Content.ReadAsStringAsync());
        using var mode = await PutSettings(env.Client, new { legalDocumentMode = "eArchiveInvoice" });
        mode.StatusCode.ShouldBe(HttpStatusCode.OK, await mode.Content.ReadAsStringAsync());
        return env;
    }

    [Test]
    public async Task EArchiveMode_OneInvoiceNoDoubleStock() {
        var env = await NewEArchiveEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "EAR1", price: 100m);
        await ReceiveStock(env, product, 10m, cost: 40m);

        var sale = await Sell(env.Client, register, [SaleLine(product, 2m)], [Pay("cash", 100m), Pay("card", 140m)]);
        sale.GetProperty("total").GetDecimal().ShouldBe(240m);
        var invoiceNumber = sale.GetProperty("invoiceNumber").GetString();
        invoiceNumber.ShouldNotBeNull();
        invoiceNumber.ShouldStartWith("EAR");
        sale.GetProperty("slip").GetProperty("notes")[0].GetString().ShouldBe("e-Arşiv fatura e-posta/SMS ile iletilecektir.");

        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            var invoice = await db.Invoices.AsNoTracking().SingleAsync();
            invoice.AffectsStock.ShouldBeFalse();
            invoice.Kind.ShouldBe(InvoiceKind.Sales);
            invoice.Status.ShouldBe(InvoiceStatus.Issued);
            invoice.Mode.ShouldBe(InvoiceMode.EArchive);
            invoice.SourceType.ShouldBe("pos-sale");
            invoice.Payable.ShouldBe(240m);

            // Tahsilatlar faturayı kapatır: perakende cari bakiyesi sıfır.
            var entries = await db.LedgerEntries.AsNoTracking().Where(e => e.CancelledAt == null).ToListAsync();
            Statement.Balance(entries).ShouldBe(0m);
        }

        // Stok yalnızca kasa belgesinde (tek çıkış); faturanın stok hareketi yok.
        (await StockMovements(env.Tenant, "pos-sale")).Count.ShouldBe(1);
        (await StockMovements(env.Tenant, "invoice")).ShouldBeEmpty();
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(100m);
        (await Balance(env.Tenant, env.CardAccount)).ShouldBe(140m);

        // Fatura iptal ucundan iptal edilemez (stoğu geri almaz).
        var invoiceId = (await AllSales(env.Tenant)).Single().InvoiceId!.Value;
        var billing   = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["fatura.read", "fatura.cancel"]);
        using (var direct = await billing.PostAsJsonAsync($"/api/erp/invoices/{invoiceId}/cancel", new { reason = "Elle iptal" })) {
            direct.StatusCode.ShouldBe(HttpStatusCode.Conflict, await direct.Content.ReadAsStringAsync());
            (await direct.ErrorCodeAsync()).ShouldBe("pos_invoice_locked");
        }

        // Satış iptali: fatura iptal, tahsilatlar iptal, stok bir kez geri alınır.
        using var cancel = await env.Client.PostAsJsonAsync($"{Api}/sales/{sale.GetProperty("id").GetGuid()}/cancel", new { reason = "Hatalı satış" });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        await using (var db = PricingTestSupport.Db(env.Tenant)) {
            (await db.Invoices.AsNoTracking().SingleAsync()).Status.ShouldBe(InvoiceStatus.Cancelled);
            Statement.Balance(await db.LedgerEntries.AsNoTracking().Where(e => e.CancelledAt == null).ToListAsync()).ShouldBe(0m);
        }

        var stock = await StockMovements(env.Tenant, "pos-sale");
        stock.Count.ShouldBe(2);
        stock.Sum(m => m.Quantity).ShouldBe(0m);
        (await Balance(env.Tenant, env.CashAccount)).ShouldBe(0m);
        (await Balance(env.Tenant, env.CardAccount)).ShouldBe(0m);
    }

    [Test]
    public async Task EArchiveMode_RequiresEBelgeStillEnabled() {
        var env = await NewEArchiveEnv();
        var (register, _) = await RegisterWithShift(env);
        var product = await Product(env.Client, "EAR2", price: 100m);
        await ReceiveStock(env, product, 10m);

        // e-Belge kapatıldı: e-Arşiv modundaki kasa satış yapamaz.
        using var off = await env.Client.PutAsJsonAsync("/api/erp/settings/ebelge", new { version = 1, value = new { enabled = false, defaultScenario = "basic", eIrsaliyeEnabled = false } });
        off.StatusCode.ShouldBe(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        (await SellFails(env.Client, register, [SaleLine(product)], [Pay("cash", 120m)], HttpStatusCode.Conflict)).ShouldBe("ebelge_required");
    }
}
