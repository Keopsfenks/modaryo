using System.Runtime.CompilerServices;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;

namespace ERP.Application.Accounting.Projectors;

/// <summary>Mahsup fişleri (elle ve iç API) olduğu gibi aktarılır: satırlardaki hesap kodları fişi girenin seçtiği kodlardır. İptalde ters fiş yazılır.</summary>
public sealed class JournalVoucherProjector(IApplicationDbContext db) : IJournalProjector {
    public const string DocumentType = "Mahsup Fişi";

    public string SourceType => "JournalVoucher";

    public async IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, AccountMapping map, [EnumeratorCancellation] CancellationToken ct) {
        var vouchers = await db.JournalVouchers.AsNoTracking()
                           .Where(v => (v.Date >= from && v.Date <= to) || v.CancelledAt != null)
                           .OrderBy(v => v.Date).ThenBy(v => v.Number)
                           .ToListAsync(ct);

        foreach (var voucher in vouchers) {
            var original = new ProjectedVoucher(voucher.Number, voucher.Date, DocumentType, voucher.Number,
                                                voucher.Lines.Select(l => l with { Description = string.IsNullOrWhiteSpace(l.Description) ? voucher.Description : l.Description }).ToList());

            if (VoucherRules.InRange(voucher.Date, from, to)) yield return original;

            if (voucher.CancelledAt is { } cancelledAt && VoucherRules.InRange(AccountingContext.DateOf(cancelledAt), from, to))
                yield return VoucherRules.Reverse(original, AccountingContext.DateOf(cancelledAt));
        }
    }
}
