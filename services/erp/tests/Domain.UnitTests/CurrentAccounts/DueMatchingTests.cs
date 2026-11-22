using NUnit.Framework;
using Shouldly;
using ERP.Domain.CurrentAccounts;

namespace ERP.Domain.UnitTests.CurrentAccounts;

public class DueMatchingTests
{
    private static LedgerEntry E(string date, decimal debit, decimal credit, string? due = null, bool cancelled = false) => new()
    {
        Date = DateOnly.Parse(date),
        DueDate = due is null ? null : DateOnly.Parse(due),
        Debit = debit,
        Credit = credit,
        Created = DateTimeOffset.UtcNow,
        CancelledAt = cancelled ? DateTimeOffset.UtcNow : null,
    };

    private static readonly DateOnly Today = DateOnly.Parse("2026-01-20");

    [Test]
    public void Due_CreditClosesOldestDebitFirst()
    {
        var entries = new[] { E("2025-12-01", 100, 0, "2026-01-01"), E("2026-01-01", 200, 0, "2026-02-01"), E("2026-01-10", 0, 150) };
        var items = DueMatching.OpenItems(entries, Today);
        items.Count.ShouldBe(1);
        items[0].Remaining.ShouldBe(150m);
        items[0].Direction.ShouldBe(DueDirection.Receivable);
        items[0].DueDate.ShouldBe(DateOnly.Parse("2026-02-01"));
    }

    [Test]
    public void Due_PartialAndOverdueDays()
    {
        var entries = new[] { E("2025-12-01", 100, 0, "2026-01-01"), E("2026-01-01", 200, 0, "2026-02-01"), E("2026-01-10", 0, 150) };
        var items = DueMatching.OpenItems(entries, DateOnly.Parse("2026-02-10"));
        items[0].DaysOverdue.ShouldBe(9);
    }

    [Test]
    public void Due_NotYetDue_HasZeroDaysOverdue()
    {
        var items = DueMatching.OpenItems(new[] { E("2026-01-01", 100, 0, "2026-03-01") }, Today);
        items[0].DaysOverdue.ShouldBe(0);
    }

    [Test]
    public void Due_SupplierSideProducesPayable()
    {
        var entries = new[] { E("2026-02-01", 0, 500, "2026-03-01"), E("2026-02-05", 200, 0) };
        var items = DueMatching.OpenItems(entries, Today);
        items.Count.ShouldBe(1);
        items[0].Direction.ShouldBe(DueDirection.Payable);
        items[0].Remaining.ShouldBe(300m);
    }

    [Test]
    public void Due_NoDueDateUsesEntryDate()
    {
        var items = DueMatching.OpenItems(new[] { E("2026-01-05", 100, 0) }, Today);
        items[0].DueDate.ShouldBe(DateOnly.Parse("2026-01-05"));
        items[0].DaysOverdue.ShouldBe(15);
    }

    [Test]
    public void Due_IgnoresCancelled()
    {
        var entries = new[] { E("2026-01-01", 100, 0), E("2026-01-02", 0, 100, cancelled: true) };
        var items = DueMatching.OpenItems(entries, Today);
        items.Count.ShouldBe(1);
        items[0].Remaining.ShouldBe(100m);
    }

    [Test]
    public void Due_FullyMatched_ReturnsEmpty()
    {
        var entries = new[] { E("2026-01-01", 100, 0), E("2026-01-02", 0, 100) };
        DueMatching.OpenItems(entries, Today).ShouldBeEmpty();
    }
}
