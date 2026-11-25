using System.Runtime.CompilerServices;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Accounting.Projectors;

/// <summary>
///     Tahsilat, ödeme, virman ve masraf/gelir fişleri (çek/senet hareketleri <see cref="InstrumentProjector" />'da). Yabancı para tutarları belgedeki kurla (yoksa TCMB kuruyla) TL'ye çevrilir.
///     Tahsilat:  B 100 | 102 (hesap)                         A 120[.cari]  (avans: 340[.cari])
///     Ödeme:     B 320[.cari]  (avans: 159[.cari])           A 100 | 102
///     Virman:    B giriş hesabı (100 | 102)                  A çıkış hesabı; iki taraf da çıkış tutarının TL karşılığıdır.
///     Masraf:    B kategori hesabı (net)   B 191 (KDV)       A hesap (100 | 102) ya da cari (320[.cari]), tutar KDV dahil
///     Gelir:     B hesap (100 | 102) ya da cari (120[.cari])   A kategori hesabı (net)   A 391 (KDV)
///     İptal edilen belge: özgün fiş + iptal tarihli ters fiş.
/// </summary>
public sealed class FinanceDocumentProjector(IApplicationDbContext db, AccountingContext context) : IJournalProjector {
    public string SourceType => "FinanceDocument";

    public async IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, AccountMapping map, [EnumeratorCancellation] CancellationToken ct) {
        var codes = await context.CodesAsync(map, ct);

        var kinds = new[] { FinanceDocumentKind.Collection, FinanceDocumentKind.Payment, FinanceDocumentKind.Transfer, FinanceDocumentKind.Expense, FinanceDocumentKind.Income };
        var documents = await db.FinanceDocuments.AsNoTracking()
                            .Where(d => kinds.Contains(d.Kind) && ((d.Date >= from && d.Date <= to) || d.CancelledAt != null))
                            .OrderBy(d => d.Date).ThenBy(d => d.Number)
                            .ToListAsync(ct);

        var accounts = await db.TreasuryAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        var categories = await db.ExpenseCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var caris = await context.CarisAsync(documents.Select(d => d.CurrentAccountId), ct);

        foreach (var document in documents) {
            var original = await BuildAsync(document, codes, accounts, categories, caris, ct);

            if (VoucherRules.InRange(document.Date, from, to)) yield return original;

            if (document.CancelledAt is { } cancelledAt && VoucherRules.InRange(AccountingContext.DateOf(cancelledAt), from, to))
                yield return VoucherRules.Reverse(original, AccountingContext.DateOf(cancelledAt));
        }
    }

    private async Task<ProjectedVoucher> BuildAsync(FinanceDocument                           document,
                                                    AccountCodes                              codes,
                                                    IReadOnlyDictionary<Guid, TreasuryAccount> accounts,
                                                    IReadOnlyDictionary<Guid, ExpenseCategory> categories,
                                                    IReadOnlyDictionary<Guid, CurrentAccount>  caris,
                                                    CancellationToken                          ct
    ) {
        var map     = codes.Map;
        var account = document.AccountId is { } a ? accounts.GetValueOrDefault(a) : null;
        var cari    = document.CurrentAccountId is { } c ? caris.GetValueOrDefault(c) : null;
        var lines   = new LineSet();
        var text    = $"{Label(document.Kind)} {document.Number}";

        switch (document.Kind) {
            case FinanceDocumentKind.Collection: {
                var tl = await context.TlAsync(document.Amount, document.Currency, document.Date, document.Rate, ct);
                var party = document.IsAdvance ? codes.Advance(cari, received: true) : codes.Customer(cari);
                lines.Debit(codes.Treasury(account), tl, text).Credit(party, tl, text, document.CurrentAccountId);
                break;
            }
            case FinanceDocumentKind.Payment: {
                var tl = await context.TlAsync(document.Amount, document.Currency, document.Date, document.Rate, ct);
                var party = document.IsAdvance ? codes.Advance(cari, received: false) : codes.Supplier(cari);
                lines.Debit(party, tl, text, document.CurrentAccountId).Credit(codes.Treasury(account), tl, text);
                break;
            }
            case FinanceDocumentKind.Transfer: {
                var tl     = await context.TlAsync(document.Amount, document.Currency, document.Date, document.Rate, ct);
                var target = document.TargetAccountId is { } t ? accounts.GetValueOrDefault(t) : null;
                lines.Debit(codes.Treasury(target), tl, text).Credit(codes.Treasury(account), tl, text);
                break;
            }
            case FinanceDocumentKind.Expense:
            case FinanceDocumentKind.Income: {
                var expense  = document.Kind == FinanceDocumentKind.Expense;
                var gross    = await context.TlAsync(document.Amount, document.Currency, document.Date, null, ct);
                var vat      = document.VatRate is { } rate and > 0m ? AccountingContext.Round(gross * rate / (100m + rate)) : 0m;
                var net      = gross - vat;
                var category = document.ExpenseCategoryId is { } id ? categories.GetValueOrDefault(id) : null;
                var code     = string.IsNullOrWhiteSpace(category?.AccountCode) ? map.ExpenseDefault : category!.AccountCode;
                var party    = account is not null ? codes.Treasury(account) : expense ? codes.Supplier(cari) : codes.Customer(cari);
                var cariId   = account is null ? document.CurrentAccountId : null;

                if (expense) lines.Debit(code, net, text).Debit(map.VatInput, vat, text).Credit(party, gross, text, cariId);
                else lines.Debit(party, gross, text, cariId).Credit(code, net, text).Credit(map.VatOutput, vat, text);
                break;
            }
        }

        return new ProjectedVoucher(document.Number, document.Date, Label(document.Kind), document.Number, lines.Lines);
    }

    private static string Label(FinanceDocumentKind kind) {
        return kind switch {
                   FinanceDocumentKind.Collection => "Tahsilat",
                   FinanceDocumentKind.Payment    => "Ödeme",
                   FinanceDocumentKind.Transfer   => "Virman",
                   FinanceDocumentKind.Expense    => "Masraf Fişi",
                   FinanceDocumentKind.Income     => "Gelir Fişi",
                   _                              => kind.ToString()
               };
    }
}
