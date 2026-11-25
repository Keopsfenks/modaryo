using System.Text.Json;
using ClosedXML.Excel;
using ERP.Web.FunctionalTests.Pricing;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Treasury.PaymentSupport;

namespace ERP.Web.FunctionalTests.Accounting;

public class ExportTests : TestBase {
    private const string ExportUrl = "/api/erp/accounting/journal.xlsx";
    private const string Range     = "from=2026-01-01&to=2026-12-31";
    private static readonly DateOnly Due = new(2026, 4, 30);

    private sealed record Row(string VoucherNo, string Date, string Account, string Description, decimal Debit, decimal Credit, string Type, string DocumentNo);

    private static Task<Env> NewExportEnv() {
        return Invoicing.InvoiceTestSupport.NewEnv(AllPermissions.Concat(TreasuryTestSupport.Permissions).Distinct().ToArray(), null, "P1", ["cek-senet"]);
    }

    private static async Task<List<Row>> Export(Env env, string query = Range) {
        using var response = await env.Client.GetAsync($"{ExportUrl}?{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var       sheet    = workbook.Worksheets.First();
        var       headers  = Enumerable.Range(1, 8).Select(c => sheet.Cell(1, c).GetString()).ToArray();
        headers.ShouldBe(["Fiş no", "Tarih", "Hesap kodu", "Açıklama", "Borç", "Alacak", "Belge türü", "Belge no"]);

        var rows = new List<Row>();
        for (var r = 2; r <= (sheet.LastRowUsed()?.RowNumber() ?? 1); r++)
            rows.Add(new Row(sheet.Cell(r, 1).GetString(), sheet.Cell(r, 2).GetString(), sheet.Cell(r, 3).GetString(), sheet.Cell(r, 4).GetString(),
                             sheet.Cell(r, 5).IsEmpty() ? 0m : (decimal)sheet.Cell(r, 5).GetDouble(), sheet.Cell(r, 6).IsEmpty() ? 0m : (decimal)sheet.Cell(r, 6).GetDouble(),
                             sheet.Cell(r, 7).GetString(), sheet.Cell(r, 8).GetString()));
        return rows;
    }

    private static List<Row> Voucher(List<Row> rows, string voucherNo) {
        return rows.Where(r => r.VoucherNo == voucherNo).ToList();
    }

    private static (string Account, decimal Debit, decimal Credit)[] Lines(List<Row> rows) {
        return rows.Select(r => (r.Account, r.Debit, r.Credit)).ToArray();
    }

    private static Task<HttpResponseMessage> Json(Env env, string path, object body) {
        return env.Client.PostAsJsonAsync($"{Base}/{path}", body);
    }

    private static async Task<Guid> CreatedId(HttpResponseMessage response, string property = "id") {
        using (response) return (await Created(response)).GetProperty(property).GetGuid();
    }

    private static async Task SetCancelDates(Guid tenant) {
        await using var db     = PricingTestSupport.Db(tenant);
        var             moment = new DateTimeOffset(2026, 6, 15, 7, 0, 0, TimeSpan.Zero);
        await db.Invoices.Where(i => i.CancelledAt != null).ExecuteUpdateAsync(s => s.SetProperty(i => i.CancelledAt, moment));
        await db.FinanceDocuments.Where(d => d.CancelledAt != null).ExecuteUpdateAsync(s => s.SetProperty(d => d.CancelledAt, moment));
        await db.JournalVouchers.Where(v => v.CancelledAt != null).ExecuteUpdateAsync(s => s.SetProperty(v => v.CancelledAt, moment));
    }

    private static async Task Move(Env env, Guid id, string to, Guid? cari = null, Guid? account = null, DateOnly? date = null) {
        using var response = await env.Client.PostAsJsonAsync($"{Base}/instruments/{id}/move",
                                                              new { to, date = (date ?? Today).ToString("yyyy-MM-dd"), currentAccountId = cari, accountId = account, note = "Not" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> Receive(Env env, string number, string kind = "check") {
        return await CreatedId(await Json(env, "instruments/receive", new {
            kind, currentAccountId = env.Customer, number, dueDate = Due.ToString("yyyy-MM-dd"), amount = 100m, currency = "TRY", drawer = "Keşideci", bankName = kind == "check" ? "Garanti" : null,
            bankBranch = "Kadıköy", accountNo = "123", date = Today.ToString("yyyy-MM-dd")
        }), "instrumentId");
    }

    private static async Task<Guid> IssueInstrument(Env env, string number, Guid? bank, string kind = "check") {
        return await CreatedId(await Json(env, "instruments/issue", new {
            kind, currentAccountId = env.Supplier, number, dueDate = Due.ToString("yyyy-MM-dd"), amount = 100m, currency = "TRY", bankAccountId = bank, date = Today.ToString("yyyy-MM-dd")
        }), "instrumentId");
    }

    /// <summary>Her belge türünden en az bir örnek (iptal edilenler dahil).</summary>
    private static async Task Populate(Env env) {
        var cash = await Account(env, "Kasa", "cash");
        var bank = await Account(env, "Banka", "bank");
        var usd  = await Account(env, "USD Banka", "bank", "USD");

        // Faturalar: satış (tevkifatlı), alış, USD satış, iade, kur farkı, iptal.
        await Invoice(env, 120m);
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20, extra: new { withholdingCode = "601" })], date: new DateOnly(2026, 3, 2)));
        await Invoice(env, 240m, kind: "Purchase");
        var cancelled = await Invoice(env, 60m);
        using (var cancel = await CancelInvoice(env, cancelled.Id)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        await CreateSeries(env, "FTR");
        var sale = await CreateAndIssue(env, Body(env, lines: [ProductLine(env, 5m, 10m)]));
        await CreateAndIssue(env, Body(env, "SalesReturn", [ProductLine(env, 2m, 10m, new { sourceLineId = sale.GetProperty("lines")[0].GetProperty("id").GetGuid() })], new { returnOfInvoiceId = sale.GetProperty("id").GetGuid() }));
        var purchase = await CreateAndIssue(env, Body(env, "Purchase", [ProductLine(env, 5m, 10m)]));
        await CreateAndIssue(env, Body(env, "PurchaseReturn", [ProductLine(env, 1m, 10m, new { sourceLineId = purchase.GetProperty("lines")[0].GetProperty("id").GetGuid() })], new { returnOfInvoiceId = purchase.GetProperty("id").GetGuid() }));

        var usdCari = await CreateCari(env.Client, "M2", customer: true, supplier: false);
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 0)], extra: new { currency = "USD", rate = 32m, currentAccountId = usdCari }, date: new DateOnly(2026, 3, 1)));
        using (var foreign = await Collect(env, usd, 100m, usdCari, "transfer", new { rate = 32.5m })) await Created(foreign);
        using var differences = await env.Client.GetAsync($"{Base}/exchange-differences");
        var       item        = (await differences.JsonAsync()).EnumerateArray().First();
        using var difference = await env.Client.PostAsJsonAsync($"{Base}/exchange-differences/invoice", new { allocationIds = item.GetProperty("allocationIds").EnumerateArray().Select(i => i.GetGuid()).ToArray() });
        await Issue(env.Client, (await Created(difference)).GetProperty("invoiceId").GetGuid());

        // Tahsilat, ödeme, avans, iptal edilen tahsilat; virmanlar.
        await CollectOk(env, cash, 50m);
        using (var payment = await Pay(env, bank, 30m, method: "transfer")) await Created(payment);
        using (var advance = await Collect(env, cash, 20m, extra: new { isAdvance = true })) await Created(advance);
        var cancelledCollection = await CollectOk(env, cash, 5m);
        using (var cancel = await CancelDocument(env, cancelledCollection)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        await CreatedId(await Json(env, "transfers", new { fromAccountId = cash, toAccountId = bank, date = Today.ToString("yyyy-MM-dd"), amount = 10m }));
        await CreatedId(await Json(env, "transfers", new { fromAccountId = usd, toAccountId = bank, date = Today.ToString("yyyy-MM-dd"), amount = 10m, rate = 32m }));

        // Masraf ve gelir fişleri.
        using var categoriesResponse = await env.Client.GetAsync($"{Base}/expense-categories");
        var       categories         = (await categoriesResponse.JsonAsync()).EnumerateArray().ToList();
        Guid Category(string code) => categories.First(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        await CreatedId(await Json(env, "expenses", new { kind = "expense", categoryId = Category("genel"), date = Today.ToString("yyyy-MM-dd"), amount = 120m, vatRate = 20m, accountId = cash }));
        await CreatedId(await Json(env, "expenses", new { kind = "income", categoryId = Category("genel-gelir"), date = Today.ToString("yyyy-MM-dd"), amount = 60m, vatRate = 20m, accountId = bank }));
        await CreatedId(await Json(env, "expenses", new { kind = "expense", categoryId = Category("kira"), date = Today.ToString("yyyy-MM-dd"), amount = 40m, currentAccountId = env.Supplier }));
        var cancelledExpense = await CreatedId(await Json(env, "expenses", new { kind = "expense", categoryId = Category("genel"), date = Today.ToString("yyyy-MM-dd"), amount = 12m, accountId = cash }));
        using (var cancel = await CancelDocument(env, cancelledExpense)) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());

        // Çek/senet yaşam döngüleri.
        var endorsed = await Receive(env, "C-1");
        await Move(env, endorsed, "endorsed", cari: env.Supplier);
        await Move(env, endorsed, "bounced");
        var collected = await Receive(env, "C-2");
        await Move(env, collected, "inCollection", account: bank);
        await Move(env, collected, "collected");
        var returned = await Receive(env, "N-1", "note");
        await Move(env, returned, "returnedToCustomer");
        var late = await Receive(env, "C-3");
        await Move(env, late, "inCollection", account: bank);
        await Move(env, late, "bounced");
        await Move(env, late, "collected", account: cash);
        var paid = await IssueInstrument(env, "OC-1", bank);
        await Move(env, paid, "paid", date: Due);
        var back = await IssueInstrument(env, "ON-1", null, "note");
        await Move(env, back, "returnedFromSupplier");

        // Mahsup fişleri.
        await CreatedId(await Json(env, "journal-vouchers", new {
            date = Today.ToString("yyyy-MM-dd"), description = "Maaş", lines = new object[] { new { accountCode = "770", debit = 100m, credit = 0m, description = "Gider" }, new { accountCode = "335", debit = 0m, credit = 100m, description = "Personel" } }
        }));
        var cancelledVoucher = await CreatedId(await Json(env, "journal-vouchers", new {
            date = Today.ToString("yyyy-MM-dd"), description = "Hatalı", lines = new object[] { new { accountCode = "770", debit = 5m, credit = 0m }, new { accountCode = "335", debit = 0m, credit = 5m } }
        }));
        using (var cancel = await env.Client.PostAsJsonAsync($"{Base}/journal-vouchers/{cancelledVoucher}/cancel", new { reason = "Hata" })) cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await SetCancelDates(env.Tenant);
    }

    [Test]
    public async Task Export_AllDocumentTypes_EveryVoucherBalanced() {
        var env = await NewExportEnv();
        await Populate(env);

        var rows = await Export(env);

        string[] expected = [
            "Satış Faturası", "Alış Faturası", "Satış İade Faturası", "Alış İade Faturası", "Kur Farkı Faturası", "Tahsilat", "Ödeme", "Virman", "Masraf Fişi", "Gelir Fişi", "Çek/Senet", "Mahsup Fişi"
        ];
        rows.Select(r => r.Type).Distinct().ShouldBeSubsetOf(expected);
        expected.ShouldBeSubsetOf(rows.Select(r => r.Type).Distinct());

        foreach (var voucher in rows.GroupBy(r => r.VoucherNo)) {
            voucher.Sum(r => r.Debit).ShouldBe(voucher.Sum(r => r.Credit), $"{voucher.Key} dengesiz");
            voucher.Sum(r => r.Debit).ShouldBeGreaterThan(0m, $"{voucher.Key} boş");
        }

        rows.Sum(r => r.Debit).ShouldBe(rows.Sum(r => r.Credit));

        // İptal edilenler: özgün fiş + iptal tarihli ters fiş; satırlar borç/alacak yer değiştirmiş olarak aynıdır.
        var reversals = rows.Where(r => r.VoucherNo.EndsWith("-IPT")).Select(r => r.VoucherNo).Distinct().ToList();
        reversals.Count.ShouldBe(4); // fatura, tahsilat, masraf, mahsup
        foreach (var reversal in reversals) {
            var original = Voucher(rows, reversal[..^4]);
            original.ShouldNotBeEmpty();
            var back = Voucher(rows, reversal);
            back.All(r => r.Date == "15.06.2026").ShouldBeTrue();
            Lines(back).OrderBy(l => l.Account).ThenBy(l => l.Debit).ToArray()
                       .ShouldBe(Lines(original).Select(l => (l.Account, Debit: l.Credit, Credit: l.Debit)).OrderBy(l => l.Account).ThenBy(l => l.Debit).ToArray());
        }
    }

    [Test]
    public async Task SalesInvoice_LinesOnMappedAccounts() {
        var env      = await NewExportEnv();
        var sale     = await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], date: new DateOnly(2026, 3, 2)));
        var purchase = await CreateAndIssue(env, Body(env, "Purchase", [FreeLine(price: 200m, vatRate: 20, extra: new { withholdingCode = "601" })], date: new DateOnly(2026, 3, 3)));

        var rows = await Export(env);

        Lines(Voucher(rows, sale.GetProperty("number").GetString()!)).ShouldBe([("120", 120m, 0m), ("600", 0m, 100m), ("391", 0m, 20m)]);
        var vat      = Dec(purchase, "totals", "vat");
        var withheld = Dec(purchase, "totals", "withheld");
        withheld.ShouldBeGreaterThan(0m);
        Lines(Voucher(rows, purchase.GetProperty("number").GetString()!)).ShouldBe([("153", 200m, 0m), ("191", vat, 0m), ("320", 0m, 200m + vat - withheld), ("360", 0m, withheld)]);
    }

    [Test]
    public async Task PaymentsAndExpenses_LinesOnMappedAccounts() {
        var env  = await NewExportEnv();
        var cash = await Account(env, "Kasa", "cash");
        var bank = await Account(env, "Banka", "bank");
        using (var collection = await Collect(env, bank, 70m, method: "transfer")) await Created(collection);
        await PayOk(env, cash, 30m);
        using var categories = await env.Client.GetAsync($"{Base}/expense-categories");
        var       category   = (await categories.JsonAsync()).EnumerateArray().First(c => c.GetProperty("code").GetString() == "banka-masrafi").GetProperty("id").GetGuid();
        await CreatedId(await Json(env, "expenses", new { kind = "expense", categoryId = category, date = Today.ToString("yyyy-MM-dd"), amount = 120m, vatRate = 20m, accountId = cash }));

        var rows = await Export(env);

        Lines(Voucher(rows, "TH2026000001")).ShouldBe([("102", 70m, 0m), ("120", 0m, 70m)]);
        Lines(Voucher(rows, "OD2026000001")).ShouldBe([("320", 30m, 0m), ("100", 0m, 30m)]);
        Lines(Voucher(rows, "MF2026000001")).ShouldBe([("780", 100m, 0m), ("191", 20m, 0m), ("100", 0m, 120m)]);
    }

    [Test]
    public async Task SubAccountPerCari_AppendsCariCode() {
        var env = await NewExportEnv();
        using (var settings = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { subAccountPerCari = true } }))
            settings.StatusCode.ShouldBe(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var cash = await Account(env, "Kasa", "cash");
        var sale = await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], date: new DateOnly(2026, 3, 2)));
        await CollectOk(env, cash, 10m);

        var rows = await Export(env);

        Lines(Voucher(rows, sale.GetProperty("number").GetString()!)).ShouldBe([("120.M1", 120m, 0m), ("600", 0m, 100m), ("391", 0m, 20m)]);
        Lines(Voucher(rows, "TH2026000001")).ShouldBe([("100", 10m, 0m), ("120.M1", 0m, 10m)]);
    }

    [Test]
    public async Task CustomMapping_Respected() {
        var env = await NewExportEnv();
        using (var settings = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { accountMapping = new { customers = "120.01", sales = "601", vatOutput = "391.20", cash = "100.01" } } }))
            settings.StatusCode.ShouldBe(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var cash = await Account(env, "Kasa", "cash");
        var sale = await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], date: new DateOnly(2026, 3, 2)));
        await CollectOk(env, cash, 10m);

        var rows = await Export(env);

        Lines(Voucher(rows, sale.GetProperty("number").GetString()!)).ShouldBe([("120.01", 120m, 0m), ("601", 0m, 100m), ("391.20", 0m, 20m)]);
        Lines(Voucher(rows, "TH2026000001")).ShouldBe([("100.01", 10m, 0m), ("120.01", 0m, 10m)]);
    }

    [Test]
    public async Task Export_RangeFiltersByDate() {
        var env = await NewExportEnv();
        await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 100m, vatRate: 20)], date: new DateOnly(2026, 3, 2)));

        (await Export(env, "from=2026-04-01&to=2026-04-30")).ShouldBeEmpty();
        (await Export(env, "from=2026-03-02&to=2026-03-02")).Count.ShouldBe(3);

        using var invalid = await env.Client.GetAsync($"{ExportUrl}?from=2026-05-01&to=2026-04-01");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task SalesInvoiceWithWithholding_PartyNetsToCariLedger() {
        var env  = await NewExportEnv();
        var sale = await CreateAndIssue(env, Body(env, lines: [FreeLine(price: 1000m, vatRate: 20, extra: new { withholdingCode = "601" })], date: new DateOnly(2026, 3, 2)));
        var vat      = Dec(sale, "totals", "vat");
        var withheld = Dec(sale, "totals", "withheld");
        withheld.ShouldBeGreaterThan(0m);

        var rows = await Export(env);

        // Tevkifatı alıcı beyan eder: 120 cari defteriyle aynı net tutarı (net + KDV - tevkifat) borçlanır, 391 yalnızca tahsil edilen KDV'yi alacaklandırır.
        Lines(Voucher(rows, sale.GetProperty("number").GetString()!)).ShouldBe([("120", 1000m + vat - withheld, 0m), ("600", 0m, 1000m), ("391", 0m, vat - withheld)]);
        (await CariBalance(env, env.Customer)).ShouldBe(1000m + vat - withheld);

        // Müşteri cari defteri kadar ödeme yapınca hem defter hem 120 hesabı sıfırlanır.
        var cash = await Account(env, "Kasa", "cash");
        await CollectOk(env, cash, 1000m + vat - withheld);
        (await CariBalance(env, env.Customer)).ShouldBe(0m);
        var all = await Export(env);
        (all.Where(r => r.Account == "120").Sum(r => r.Debit) - all.Where(r => r.Account == "120").Sum(r => r.Credit)).ShouldBe(0m);
    }

    [Test]
    public async Task PartyAccountFollowsCariRole_NotDocumentKind() {
        var env = await NewExportEnv();
        using (var settings = await env.Client.PutAsJsonAsync("/api/erp/settings/finans", new { version = 0, value = new { subAccountPerCari = true } }))
            settings.StatusCode.ShouldBe(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var cash = await Account(env, "Kasa", "cash");

        // Tedarikçiden iade tahsilatı 320'ye, müşteriye iade ödemesi 120'ye yazılır.
        var supplierRefund = await CreatedId(await Collect(env, cash, 40m, env.Supplier));
        var customerRefund = await CreatedId(await Pay(env, cash, 25m, env.Customer));
        using var categories = await env.Client.GetAsync($"{Base}/expense-categories");
        var       category   = (await categories.JsonAsync()).EnumerateArray().First(c => c.GetProperty("code").GetString() == "kur-farki").GetProperty("id").GetGuid();
        var       expense    = await CreatedId(await Json(env, "expenses", new { kind = "expense", categoryId = category, date = Today.ToString("yyyy-MM-dd"), amount = 50m, currentAccountId = env.Customer }));

        var rows = await Export(env);
        var documents = await Documents(env.Tenant);
        string No(Guid id) => documents.Single(d => d.Id == id).Number;

        Lines(Voucher(rows, No(supplierRefund))).ShouldBe([("100", 40m, 0m), ("320.T1", 0m, 40m)]);
        Lines(Voucher(rows, No(customerRefund))).ShouldBe([("120.M1", 25m, 0m), ("100", 0m, 25m)]);
        // Müşteriye yazılan kur farkı gideri 320.M1 değil 120.M1 kodunu alacaklandırır; cari artığı 120'de kapanır.
        Lines(Voucher(rows, No(expense))).ShouldBe([("656", 50m, 0m), ("120.M1", 0m, 50m)]);

        // Her cari tek bir ana hesapta toplanır: müşteri 120.M1 (net 25 - 50), tedarikçi 320.T1.
        rows.Where(r => r.Account.EndsWith(".M1")).Select(r => r.Account).Distinct().ShouldBe(["120.M1"]);
        rows.Where(r => r.Account.EndsWith(".T1")).Select(r => r.Account).Distinct().ShouldBe(["320.T1"]);
        (await CariBalance(env, env.Customer)).ShouldBe(-25m);
        (rows.Where(r => r.Account == "120.M1").Sum(r => r.Debit) - rows.Where(r => r.Account == "120.M1").Sum(r => r.Credit)).ShouldBe(-25m);
    }

    [Test]
    public async Task Export_RequiresPermission() {
        var env = await NewExportEnv();
        using var limited = await OtherClient(env, "finans.read", "muhasebe.manage");

        using var forbidden = await limited.GetAsync($"{ExportUrl}?{Range}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var exporter = await OtherClient(env, "muhasebe.export");
        using var allowed  = await exporter.GetAsync($"{ExportUrl}?{Range}");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
