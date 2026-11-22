using NUnit.Framework;
using Shouldly;
using ERP.Domain.CurrentAccounts;

namespace ERP.Domain.UnitTests.CurrentAccounts;

public class StatementTests
{
    private static LedgerEntry E(string date, decimal debit, decimal credit, bool cancelled = false, int createdOrder = 0) => new()
    {
        Date = DateOnly.Parse(date),
        Debit = debit,
        Credit = credit,
        Created = new DateTimeOffset(2026, 1, 1, 0, 0, createdOrder, TimeSpan.Zero),
        CancelledAt = cancelled ? DateTimeOffset.UtcNow : null,
        CancelReason = cancelled ? "hatalı" : null,
    };

    [Test]
    public void Balance_ExcludesCancelled()
    {
        var entries = new[] { E("2026-01-01", 100, 0), E("2026-01-02", 50, 0, cancelled: true), E("2026-01-03", 0, 30) };
        Statement.Balance(entries).ShouldBe(70m);
    }

    [Test]
    public void Balance_IsExactDecimal()
    {
        Statement.Balance(new[] { E("2026-01-01", 0.10m, 0), E("2026-01-01", 0.20m, 0) }).ShouldBe(0.30m);
    }

    [Test]
    public void Statement_OpeningBalanceFromEntriesBeforeFrom()
    {
        var entries = new[] { E("2026-01-01", 100, 0), E("2026-01-05", 0, 40), E("2026-01-06", 10, 0, cancelled: true), E("2026-02-01", 5, 0) };
        var r = Statement.Build(entries, DateOnly.Parse("2026-02-01"), DateOnly.Parse("2026-02-28"));
        r.OpeningBalance.ShouldBe(60m);
        r.Rows.Count.ShouldBe(1);
        r.Rows[0].RunningBalance.ShouldBe(65m);
        r.ClosingBalance.ShouldBe(65m);
    }

    [Test]
    public void Statement_RowsIncludeBothBoundaryDates()
    {
        var entries = new[] { E("2026-01-31", 1, 0), E("2026-02-01", 2, 0), E("2026-02-28", 3, 0), E("2026-03-01", 4, 0) };
        var r = Statement.Build(entries, DateOnly.Parse("2026-02-01"), DateOnly.Parse("2026-02-28"));
        r.Rows.Select(x => x.Debit).ShouldBe(new[] { 2m, 3m });
        r.OpeningBalance.ShouldBe(1m);
    }

    [Test]
    public void Statement_CancelledRowKeepsPreviousRunningBalance()
    {
        var entries = new[] { E("2026-02-01", 100, 0, createdOrder: 1), E("2026-02-02", 50, 0, cancelled: true, createdOrder: 2), E("2026-02-03", 0, 30, createdOrder: 3) };
        var r = Statement.Build(entries, DateOnly.Parse("2026-02-01"), DateOnly.Parse("2026-02-28"));
        r.Rows.Select(x => x.RunningBalance).ShouldBe(new[] { 100m, 100m, 70m });
        r.Rows[1].Cancelled.ShouldBeTrue();
        r.Rows[1].CancelReason.ShouldBe("hatalı");
    }

    [Test]
    public void Statement_TotalsExcludeCancelled()
    {
        var entries = new[] { E("2026-02-01", 100, 0), E("2026-02-02", 50, 0, cancelled: true), E("2026-02-03", 0, 30), E("2026-02-04", 0, 7, cancelled: true) };
        var r = Statement.Build(entries, DateOnly.Parse("2026-02-01"), DateOnly.Parse("2026-02-28"));
        r.TotalDebit.ShouldBe(100m);
        r.TotalCredit.ShouldBe(30m);
        r.ClosingBalance.ShouldBe(70m);
    }
}
