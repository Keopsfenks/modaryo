using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory.Documents;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Journal;

public sealed record JournalLineInput(string AccountCode, decimal Debit, decimal Credit, string? Description, Guid? CurrentAccountId);

public sealed record VoucherResult(Guid Id, string Number);

public sealed record VoucherInput(
    DateOnly                        Date,
    string                          Description,
    string                          SourceType,
    Guid?                           SourceId,
    string?                         ExternalCaller,
    IReadOnlyList<JournalLineInput> Lines);

public static class JournalRules {
    /// <summary>Satır biçimi: hesap kodu, ya borç ya alacak (tam biri sıfırdan büyük), en çok 2 ondalık.</summary>
    public static void AddLineRules<T>(this AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, IEnumerable<JournalLineInput>>> lines) {
        validator.RuleFor(lines).NotEmpty().WithMessage("Fişte en az iki satır olmalıdır.")
            .Must(l => l.Count() is >= 2 and <= 500).WithMessage("Fişte en az 2, en çok 500 satır olabilir.");
        validator.RuleForEach(lines).ChildRules(l => {
            l.RuleFor(x => x.AccountCode).Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length <= 30).WithMessage("Hesap kodu zorunludur (en çok 30 karakter).");
            l.RuleFor(x => x.Debit).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(CardRules.MaxAmount).WithMessage("Borç negatif olamaz.");
            l.RuleFor(x => x.Credit).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(CardRules.MaxAmount).WithMessage("Alacak negatif olamaz.");
            l.RuleFor(x => x.Debit).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
            l.RuleFor(x => x.Credit).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
            l.RuleFor(x => x).Must(x => (x.Debit > 0m) != (x.Credit > 0m)).WithName("Debit").WithMessage("Her satırda borç ya da alacaktan tam biri sıfırdan büyük olmalıdır.");
            l.RuleFor(x => x.Description).MaximumLength(500).WithMessage("Satır açıklaması en çok 500 karakter olabilir.");
        });
    }
}

/// <summary>
///     Mahsup fişini yazar (elle ve iç API). Çağıran açık bir işlem içinde olmalı ve işlemi kendisi onaylamalıdır.
///     Sıra: cari kilitleri (kimlik sırasıyla), dönem kilidi, numara, fiş, cari satırları, otomatik eşleştirme, olay.
///     Cari bağlı satırlar carinin defterine <see cref="LedgerEntrySource.Voucher" /> kaynaklı hareket yazar (borç satırı carinin borcu).
/// </summary>
public sealed class JournalVoucherPoster(
    IApplicationDbContext db,
    IPeriodLock           periodLock,
    AutoAllocator         auto,
    IOutbox               outbox
) {
    public async Task<VoucherResult> PostAsync(VoucherInput input, CancellationToken ct) {
        var lines = input.Lines.Select(l => new JournalLine(l.AccountCode.Trim(), l.Debit, l.Credit, CardRules.Clean(l.Description) ?? string.Empty, l.CurrentAccountId)).ToList();
        if (lines.Count < 2 || lines.Any(l => (l.Debit > 0m) == (l.Credit > 0m) || l.Debit < 0m || l.Credit < 0m))
            throw TreasuryErrors.VoucherInvalid("Fişte en az iki satır olmalı; her satırda borç ya da alacaktan tam biri sıfırdan büyük olmalıdır.");
        if (lines.Sum(l => l.Debit) != lines.Sum(l => l.Credit)) throw TreasuryErrors.VoucherUnbalanced();

        var cariIds = lines.Where(l => l.CurrentAccountId is not null).Select(l => l.CurrentAccountId!.Value).Distinct().OrderBy(id => id).ToList();
        foreach (var id in cariIds)
            if (!await db.LockCurrentAccountAsync(id, ct))
                throw new NotFoundException(id.ToString(), "CurrentAccount");

        await periodLock.EnsureOpenAsync(input.Date, ct);

        var caris = await db.CurrentAccounts.AsNoTracking().Where(c => cariIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var cari in caris.Values) {
            if (!cari.IsActive) throw CariErrors.AccountInactive();
            if (cari.Currency != "TRY") throw TreasuryErrors.VoucherCariCurrency();
        }

        var number  = await DocumentNumbers.NextAsync(db, "MH", input.Date, ct);
        var voucher = new JournalVoucher {
                                             Number         = number,
                                             Date           = input.Date,
                                             Description    = CardRules.Clean(input.Description) ?? string.Empty,
                                             SourceType     = input.SourceType,
                                             SourceId       = input.SourceId,
                                             ExternalCaller = input.ExternalCaller,
                                             Lines          = lines
                                         };
        db.JournalVouchers.Add(voucher);

        foreach (var line in lines.Where(l => l.CurrentAccountId is not null)) {
            var debit  = line.Debit > 0m;
            var amount = debit ? line.Debit : line.Credit;
            TreasuryPosting.CariEntry(db, caris[line.CurrentAccountId!.Value], debit, amount, input.Date, LedgerEntrySource.Voucher, voucher.Id, number,
                                      string.IsNullOrEmpty(line.Description) ? $"Mahsup: {voucher.Description}" : line.Description);
        }

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (IsSourceIndexViolation(ex)) {
            throw TreasuryErrors.VoucherSourceExists();
        }

        foreach (var id in cariIds) await auto.RebuildAsync(id, ct);

        outbox.Enqueue("journal_voucher.posted", new {
                                                         voucherId      = voucher.Id,
                                                         number,
                                                         date           = input.Date,
                                                         sourceType     = input.SourceType,
                                                         sourceId       = input.SourceId,
                                                         externalCaller = input.ExternalCaller,
                                                         totalDebit     = lines.Sum(l => l.Debit)
                                                     });
        await db.SaveChangesAsync(ct);
        return new VoucherResult(voucher.Id, number);
    }

    /// <summary>Yalnızca dış servis kaynağı indeksi (aynı kaynak için ikinci fiş) çakışması; numara gibi başka çakışmalar olduğu gibi yükselir.</summary>
    private static bool IsSourceIndexViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return TreasuryErrors.IsUniqueViolation(ex)
               && inner?.GetType().GetProperty("ConstraintName")?.GetValue(inner) is string constraint
               && constraint.Contains("ExternalCaller", StringComparison.Ordinal);
    }
}
