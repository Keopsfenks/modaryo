using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts;

public static class EntryRules {
    public const string OpeningDescription = "Açılış bakiyesi";

    /// <summary>Elle girilen bir hareketi oluşturur (kart açılışı ve hareket girişi aynı yolu kullanır).</summary>
    public static LedgerEntry NewEntry(CurrentAccount  account,
                                       LedgerEntryType type,
                                       DateOnly        date,
                                       DateOnly?       dueDate,
                                       string?         documentNo,
                                       string?         description,
                                       decimal         amount,
                                       bool            debit
    ) {
        return new LedgerEntry {
                                   CurrentAccountId = account.Id,
                                   Type             = type,
                                   Source           = LedgerEntrySource.Manual,
                                   Date             = date,
                                   DueDate          = dueDate,
                                   DocumentNo       = CardRules.Clean(documentNo),
                                   Description = CardRules.Clean(description) ??
                                                 (type == LedgerEntryType.Opening ? OpeningDescription : null),
                                   Debit    = debit ? amount : 0,
                                   Credit   = debit ? 0 : amount,
                                   Currency = account.Currency
                               };
    }

    /// <summary>Hareketi tek vade satırıyla birlikte bağlama ekler (tutar borç ya da alacak, vade DueDate ?? Date).</summary>
    public static void Add(IApplicationDbContext db, LedgerEntry entry) {
        db.LedgerEntries.Add(entry);
        db.LedgerDueLines.Add(LedgerDueLine.ForEntry(entry));
    }

    public static StatementRow ToRow(LedgerEntry e, decimal runningBalance) {
        return new StatementRow(e.Id, e.Date, e.Type, e.DocumentNo, e.Description,
                                e.Debit, e.Credit, e.DueDate, runningBalance, e.IsCancelled, e.CancelReason, e.Source, e.SourceId);
    }
}