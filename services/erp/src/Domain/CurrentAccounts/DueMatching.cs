namespace ERP.Domain.CurrentAccounts;

public sealed record OpenItem(
    Guid         EntryId,
    DateOnly     Date,
    DateOnly     DueDate,
    DueDirection Direction,
    decimal      Remaining,
    int          DaysOverdue,
    Guid         DueLineId = default,
    int          Sequence  = 1
);

/// <summary>Eşleştirmeye giren vade satırı; sıralama ve iptal bilgisi satırın hareketinden gelir.</summary>
public sealed record DueLineRef(
    LedgerDueLine  Line,
    DateOnly       EntryDate,
    DateTimeOffset EntryCreated,
    bool           EntryCancelled
);

/// <summary>Otomatik eşleştirmenin önerdiği kapatma: borç satırı, alacak satırı, tutar.</summary>
public sealed record AllocationMatch(
    Guid    DebitLineId,
    Guid    CreditLineId,
    decimal Amount
);

public static class DueMatching {
    /// <summary>FIFO eşleştirme: alacaklar en eski borçları kapatır; kalan açık kalemler döner.</summary>
    public static IReadOnlyList<OpenItem> OpenItems(IEnumerable<LedgerEntry> entries, DateOnly today) {
        var active  = entries.Where(e => !e.IsCancelled).ToList();
        var debits  = Order(active.Where(e => e.Debit  > 0).Select(e => new Slot(e, e.Debit)));
        var credits = Order(active.Where(e => e.Credit > 0).Select(e => new Slot(e, e.Credit)));

        int d = 0, c = 0;
        while (d < debits.Count && c < credits.Count) {
            var m = Math.Min(debits[d].Remaining, credits[c].Remaining);
            debits[d].Remaining  -= m;
            credits[c].Remaining -= m;
            if (debits[d].Remaining == 0) d++;

            if (credits[c].Remaining == 0) c++;
        }

        var result = new List<OpenItem>();
        result.AddRange(debits.Where(s => s.Remaining  > 0).Select(s => ToItem(s, DueDirection.Receivable, today)));
        result.AddRange(credits.Where(s => s.Remaining > 0).Select(s => ToItem(s, DueDirection.Payable,    today)));
        return result;
    }

    /// <summary>
    ///     Saklı satırlar üzerinde otomatik eşleştirme. İptal edilmiş hareketlerin satırları dışarıda kalır.
    ///     Elle eşleştirmeler önce tutarlarını iki satırdan da düşer; kalan tutarlar FIFO ile
    ///     (vade, hareket tarihi, oluşturulma, sıra) eşleştirilir. Yalnızca otomatik eşleştirmeleri döndürür.
    /// </summary>
    public static IReadOnlyList<AllocationMatch> AutoAllocations(IEnumerable<DueLineRef>       lines,
                                                                 IEnumerable<LedgerAllocation> manual
    ) {
        var slots = Remaining(lines, manual);
        var debits = OrderLines(slots.Values.Where(s => s.Ref.Line.Direction == DueDirection.Receivable &&
                                                        s.Remaining          > 0));
        var credits = OrderLines(slots.Values.Where(s => s.Ref.Line.Direction == DueDirection.Payable &&
                                                         s.Remaining          > 0));

        var matches = new List<AllocationMatch>();
        int d       = 0, c = 0;
        while (d < debits.Count && c < credits.Count) {
            var m = Math.Min(debits[d].Remaining, credits[c].Remaining);
            debits[d].Remaining  -= m;
            credits[c].Remaining -= m;
            matches.Add(new AllocationMatch(debits[d].Ref.Line.Id, credits[c].Ref.Line.Id, m));
            if (debits[d].Remaining == 0) d++;

            if (credits[c].Remaining == 0) c++;
        }

        return matches;
    }

    /// <summary>
    ///     Saklı satırlar eksi eşleştirmeler: kalan tutarı olan satırlar açık kalemdir. Sıra okuma anındaki
    ///     FIFO ile aynıdır (önce tahsil edilecekler, sonra ödenecekler; her biri vade sırasıyla).
    /// </summary>
    public static IReadOnlyList<OpenItem> OpenItems(IEnumerable<DueLineRef>       lines,
                                                    IEnumerable<LedgerAllocation> allocations,
                                                    DateOnly                      today
    ) {
        var slots = Remaining(lines, allocations);
        var open  = slots.Values.Where(s => s.Remaining > 0).ToList();

        var result = new List<OpenItem>();
        result.AddRange(OrderLines(open.Where(s => s.Ref.Line.Direction == DueDirection.Receivable))
                            .Select(s => ToItem(s, today)));
        result.AddRange(OrderLines(open.Where(s => s.Ref.Line.Direction == DueDirection.Payable))
                            .Select(s => ToItem(s, today)));
        return result;
    }

    /// <summary>Etkin satırların eşleştirmeler düşüldükten sonraki kalanı (iptal edilmiş satıra dokunan eşleştirme yok sayılır).</summary>
    private static Dictionary<Guid, LineSlot> Remaining(IEnumerable<DueLineRef>       lines,
                                                        IEnumerable<LedgerAllocation> allocations
    ) {
        var slots = lines.Where(l => !l.EntryCancelled).ToDictionary(l => l.Line.Id, l => new LineSlot(l));
        foreach (var a in allocations) {
            if (!slots.TryGetValue(a.DebitLineId, out var debit) || !slots.TryGetValue(a.CreditLineId, out var credit))
                continue;

            debit.Remaining  -= a.Amount;
            credit.Remaining -= a.Amount;
        }

        return slots;
    }

    private static List<LineSlot> OrderLines(IEnumerable<LineSlot> slots) {
        return slots.OrderBy(s => s.Ref.Line.DueDate).ThenBy(s => s.Ref.EntryDate).ThenBy(s => s.Ref.EntryCreated)
            .ThenBy(s => s.Ref.Line.Sequence).ThenBy(s => s.Ref.Line.Id).ToList();
    }

    private static OpenItem ToItem(LineSlot s, DateOnly today) {
        var line = s.Ref.Line;
        return new OpenItem(line.EntryId, s.Ref.EntryDate, line.DueDate, line.Direction, s.Remaining,
                            Math.Max(0, today.DayNumber - line.DueDate.DayNumber), line.Id, line.Sequence);
    }

    private static List<Slot> Order(IEnumerable<Slot> slots) {
        return slots.OrderBy(s => s.Due).ThenBy(s => s.Entry.Date).ThenBy(s => s.Entry.Created).ToList();
    }

    private static OpenItem ToItem(Slot s, DueDirection direction, DateOnly today) {
        return new OpenItem(s.Entry.Id, s.Entry.Date, s.Due, direction, s.Remaining,
                            Math.Max(0, today.DayNumber - s.Due.DayNumber));
    }

    private sealed class Slot(
        LedgerEntry entry,
        decimal     remaining
    ) {
        public LedgerEntry Entry     { get; }      = entry;
        public decimal     Remaining { get; set; } = remaining;
        public DateOnly    Due       => Entry.DueDate ?? Entry.Date;
    }

    private sealed class LineSlot(
        DueLineRef line
    ) {
        public DueLineRef Ref       { get; }      = line;
        public decimal    Remaining { get; set; } = line.Line.Amount;
    }
}