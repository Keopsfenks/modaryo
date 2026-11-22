namespace ERP.Domain.CurrentAccounts;

public sealed record OpenItem(
    Guid EntryId, DateOnly Date, DateOnly DueDate, DueDirection Direction, decimal Remaining, int DaysOverdue);

public static class DueMatching
{
    private sealed class Slot(LedgerEntry entry, decimal remaining)
    {
        public LedgerEntry Entry { get; } = entry;
        public decimal Remaining { get; set; } = remaining;
        public DateOnly Due => Entry.DueDate ?? Entry.Date;
    }

    /// <summary>FIFO eşleştirme: alacaklar en eski borçları kapatır; kalan açık kalemler döner.</summary>
    public static IReadOnlyList<OpenItem> OpenItems(IEnumerable<LedgerEntry> entries, DateOnly today)
    {
        var active = entries.Where(e => !e.IsCancelled).ToList();
        var debits = Order(active.Where(e => e.Debit > 0).Select(e => new Slot(e, e.Debit)));
        var credits = Order(active.Where(e => e.Credit > 0).Select(e => new Slot(e, e.Credit)));

        int d = 0, c = 0;
        while (d < debits.Count && c < credits.Count)
        {
            var m = Math.Min(debits[d].Remaining, credits[c].Remaining);
            debits[d].Remaining -= m;
            credits[c].Remaining -= m;
            if (debits[d].Remaining == 0)
            {
                d++;
            }

            if (credits[c].Remaining == 0)
            {
                c++;
            }
        }

        var result = new List<OpenItem>();
        result.AddRange(debits.Where(s => s.Remaining > 0).Select(s => ToItem(s, DueDirection.Receivable, today)));
        result.AddRange(credits.Where(s => s.Remaining > 0).Select(s => ToItem(s, DueDirection.Payable, today)));
        return result;
    }

    private static List<Slot> Order(IEnumerable<Slot> slots)
        => slots.OrderBy(s => s.Due).ThenBy(s => s.Entry.Date).ThenBy(s => s.Entry.Created).ToList();

    private static OpenItem ToItem(Slot s, DueDirection direction, DateOnly today)
        => new(s.Entry.Id, s.Entry.Date, s.Due, direction, s.Remaining, Math.Max(0, today.DayNumber - s.Due.DayNumber));
}
