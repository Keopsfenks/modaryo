namespace ERP.Domain.Sales;

public static class RecurrenceCalendar {
    /// <summary>Başlangıçtan itibaren dönem gün tarihlerini (start ≤ tarih ≤ min(end, upTo)) üretir.</summary>
    public static IReadOnlyList<DateOnly> DueDates(RecurrenceInterval interval, int day, DateOnly start, DateOnly? end, DateOnly upTo) {
        if (day is < 1 or > 28) throw new ArgumentOutOfRangeException(nameof(day), "Ayın günü 1-28 arasında olmalıdır.");
        var step  = interval switch { RecurrenceInterval.Monthly => 1, RecurrenceInterval.Quarterly => 3, _ => 12 };
        var limit = end.HasValue && end.Value < upTo ? end.Value : upTo;
        var list  = new List<DateOnly>();
        var first = new DateOnly(start.Year, start.Month, 1);
        for (var i = 0; ; i++) {
            var m    = first.AddMonths(i * step);
            var date = new DateOnly(m.Year, m.Month, day);
            if (date > limit) break;
            if (date >= start) list.Add(date);
        }
        return list;
    }
}
