using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury;

/// <summary>Belgelerin ortak yazma yardımcıları: cari defter satırı ve hesap hareketi.</summary>
public static class TreasuryPosting {
    /// <summary>Cari defterine tek vade satırlı hareket yazar (borç ya da alacak; tutar carinin para birimindedir).</summary>
    public static LedgerEntry CariEntry(IApplicationDbContext db,
                                        CurrentAccount        cari,
                                        bool                  debit,
                                        decimal               amount,
                                        DateOnly              date,
                                        LedgerEntrySource     source,
                                        Guid                  sourceId,
                                        string                documentNo,
                                        string                description
    ) {
        var entry = new LedgerEntry {
                                        CurrentAccountId = cari.Id,
                                        Type             = debit ? LedgerEntryType.DebitNote : LedgerEntryType.CreditNote,
                                        Source           = source,
                                        SourceId         = sourceId,
                                        Date             = date,
                                        DueDate          = date,
                                        DocumentNo       = documentNo,
                                        Description      = description,
                                        Debit            = debit ? amount : 0m,
                                        Credit           = debit ? 0m : amount,
                                        Currency         = cari.Currency
                                    };
        EntryRules.Add(db, entry);
        return entry;
    }

    /// <summary>Hesap hareketi: tutar işaretlidir (giriş +, çıkış -) ve hesabın para birimindedir.</summary>
    public static void Movement(IApplicationDbContext db, TreasuryAccount account, Guid documentId, DateOnly date, decimal signedAmount, string? description) {
        db.TreasuryMovements.Add(new TreasuryMovement {
                                                          AccountId   = account.Id,
                                                          DocumentId  = documentId,
                                                          Date        = date,
                                                          Amount      = signedAmount,
                                                          Currency    = account.Currency,
                                                          Description = description
                                                      });
    }

    public static decimal Round(decimal value) {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
