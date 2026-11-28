using System.Runtime.CompilerServices;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;
using ERP.Domain.Pos;

namespace ERP.Application.Accounting.Projectors;

/// <summary>
///     Yazar kasa modundaki POS satış ve iadeleri (e-Arşiv modundaki satışlar fatura ve tahsilat fişleriyle zaten yazılır, burada yer almaz).
///     Satış:  B 100 (nakit, para üstü düşülmüş)   B 102 (kart)   B 120[.cari] (veresiye)   B 120[.cari] (değişimde iade mahsubu)    A 600 (net)   A 391 (KDV)
///     İade:   B 610 (net)   B 391 (KDV)    A 100 (nakit iade)   A 102 (karta iade)   A 120[.cari] (veresiye borcundan düşülen)   A 120[.cari] (değişimde yeni satışa mahsup)
///     İptal edilen satış: özgün fiş + iptal tarihli ters fiş. Mahsup satırları satış ve iade fişlerinde birbirini götürür.
/// </summary>
public sealed class PosSaleProjector(IApplicationDbContext db, AccountingContext context) : IJournalProjector {
    public string SourceType => "PosSale";

    public async IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, AccountMapping map, [EnumeratorCancellation] CancellationToken ct) {
        var codes = await context.CodesAsync(map, ct);

        var sales = await db.PosSales.AsNoTracking()
                            .Where(s => s.Mode == LegalDocumentMode.FiscalReceipt && ((s.Date >= from && s.Date <= to) || s.CancelledAt != null))
                            .OrderBy(s => s.Date).ThenBy(s => s.Number).ToListAsync(ct);
        var returns = await db.PosReturns.AsNoTracking()
                              .Where(r => r.Mode == LegalDocumentMode.FiscalReceipt && r.Date >= from && r.Date <= to)
                              .OrderBy(r => r.Date).ThenBy(r => r.Number).ToListAsync(ct);
        var saleIds = returns.Select(r => r.SaleId).Distinct().ToList();
        var returned = saleIds.Count == 0 ? [] : await db.PosSales.AsNoTracking().Where(s => saleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.CurrentAccountId, ct);
        var caris = await context.CarisAsync(sales.Select(s => (Guid?)s.CurrentAccountId).Concat(returned.Values.Select(v => (Guid?)v)), ct);

        foreach (var sale in sales) {
            var original = BuildSale(sale, codes, caris.GetValueOrDefault(sale.CurrentAccountId));
            if (VoucherRules.InRange(sale.Date, from, to)) yield return original;
            if (sale.CancelledAt is { } cancelledAt && VoucherRules.InRange(AccountingContext.DateOf(cancelledAt), from, to))
                yield return VoucherRules.Reverse(original, AccountingContext.DateOf(cancelledAt));
        }

        foreach (var ret in returns)
            yield return BuildReturn(ret, codes, returned.TryGetValue(ret.SaleId, out var cari) ? caris.GetValueOrDefault(cari) : null, cari);
    }

    private static ProjectedVoucher BuildSale(PosSale sale, AccountCodes codes, ERP.Domain.CurrentAccounts.CurrentAccount? cari) {
        var map   = codes.Map;
        var text  = $"POS Satış {sale.Number}";
        var party = codes.Customer(cari);
        var lines = new LineSet();
        lines.Debit(map.Cash, sale.CashNet, text).Debit(map.Bank, sale.CardAmount, text)
             .Debit(party, sale.OnAccountAmount, text, sale.CurrentAccountId).Debit(party, sale.ReturnCreditAmount, text + " (iade mahsubu)", sale.CurrentAccountId)
             .Credit(map.Sales, sale.Subtotal, text).Credit(map.VatOutput, sale.VatTotal, text);
        return new ProjectedVoucher(sale.Number, sale.Date, "POS Satış", sale.Number, lines.Lines);
    }

    private static ProjectedVoucher BuildReturn(PosReturn ret, AccountCodes codes, ERP.Domain.CurrentAccounts.CurrentAccount? cari, Guid cariId) {
        var map   = codes.Map;
        var text  = $"POS İade {ret.Number}";
        var party = codes.Customer(cari);
        var lines = new LineSet();
        lines.Debit(map.SalesReturns, ret.Net, text).Debit(map.VatOutput, ret.Vat, text)
             .Credit(map.Cash, ret.CashRefund, text).Credit(map.Bank, ret.CardRefund, text)
             .Credit(party, ret.OnAccountCredit, text, cariId).Credit(party, ret.ExchangeCredit, text + " (değişim mahsubu)", cariId);
        return new ProjectedVoucher(ret.Number, ret.Date, "POS İade", ret.Number, lines.Lines);
    }
}
