using ERP.Domain.Sales;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Sales;

public class RecurrenceCalendarTests {
    [Test]
    public void Recurrence_MonthlyDay28_AcrossFebruary() {
        var d = RecurrenceCalendar.DueDates(RecurrenceInterval.Monthly, 28, new DateOnly(2027, 1, 1), null, new DateOnly(2027, 3, 31));
        d.ShouldBe(new[] { new DateOnly(2027, 1, 28), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 28) });
    }

    [Test]
    public void Recurrence_Quarterly() {
        var d = RecurrenceCalendar.DueDates(RecurrenceInterval.Quarterly, 10, new DateOnly(2027, 1, 15), null, new DateOnly(2027, 12, 31));
        d.ShouldBe(new[] { new DateOnly(2027, 4, 10), new DateOnly(2027, 7, 10), new DateOnly(2027, 10, 10) });
    }

    [Test]
    public void Recurrence_RespectsEndDate_AndYearly() {
        var d = RecurrenceCalendar.DueDates(RecurrenceInterval.Yearly, 5, new DateOnly(2026, 3, 1), new DateOnly(2028, 3, 1), new DateOnly(2030, 1, 1));
        d.ShouldBe(new[] { new DateOnly(2026, 3, 5), new DateOnly(2027, 3, 5) });
    }
}
