using System.Globalization;
using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts;

/// <summary>"current-account.entry-added" olayının data gövdesi (JSON'da snake_case; bkz. contracts/README.md).</summary>
public sealed record EntryAddedData(
    Guid    CurrentAccountId,
    Guid    EntryId,
    string  EntryType,
    string  Kind,
    string  Amount,
    string  Currency,
    string  Date,
    string? DueDate,
    string? Description
);

public static class EntryEvents {
    public const string EntryAdded = "current-account.entry-added";

    private static string EntryType(LedgerEntryType type) {
        return type switch {
                   LedgerEntryType.Opening   => "opening",
                   LedgerEntryType.DebitNote => "debit-note",
                   _                         => "credit-note"
               };
    }

    /// <summary>Her yeni hareket (açılış dahil) için bir olay kuyruğa alınır. entry_type: opening | debit-note | credit-note; kind: hareketin yönü (debit | credit).</summary>
    public static void EnqueueEntryAdded(this IOutbox outbox, LedgerEntry entry) {
        var debit = entry.Debit > 0;
        outbox.Enqueue(EntryAdded, new EntryAddedData(
                                                      entry.CurrentAccountId,
                                                      entry.Id,
                                                      EntryType(entry.Type),
                                                      debit ? "debit" : "credit",
                                                      (debit ? entry.Debit : entry.Credit).ToString("0.00",
                                                          CultureInfo.InvariantCulture),
                                                      entry.Currency,
                                                      entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                                      entry.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                                                      entry.Description));
    }
}
