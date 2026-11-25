using ERP.Domain.Sales;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Sales;

public class InstallmentSplitterTests {
    [Test]
    public void Equal_7360_DownPlusTwo_Hali() {
        var rows = InstallmentSplitter.Equal(7360m, 2453.33m, 2, new DateOnly(2026, 12, 15));
        rows.Count.ShouldBe(2);
        rows[0].Amount.ShouldBe(2453.33m);
        rows[1].Amount.ShouldBe(2453.34m);
        rows[0].DueDate.ShouldBe(new DateOnly(2026, 12, 15));
        rows[1].DueDate.ShouldBe(new DateOnly(2027, 1, 15));
        (rows.Sum(r => r.Amount) + 2453.33m).ShouldBe(7360m);
    }

    [Test]
    public void Equal_MonthEndClamps() {
        var rows = InstallmentSplitter.Equal(300m, 0m, 3, new DateOnly(2027, 1, 31));
        rows.Select(r => r.DueDate).ShouldBe(new[] { new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31) });
    }

    [Test]
    public void Equal_InvalidCount_Throws() {
        Should.Throw<ArgumentOutOfRangeException>(() => InstallmentSplitter.Equal(100m, 0m, 37, new DateOnly(2027, 1, 1)));
    }
}
