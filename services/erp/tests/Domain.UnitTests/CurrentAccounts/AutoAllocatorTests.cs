using ERP.Domain.CurrentAccounts;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.CurrentAccounts;

public class AutoAllocatorTests {
    private static readonly DateOnly Today = DateOnly.Parse("2026-01-20");

    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    private static int _tick;

    private static LedgerEntry E(string  date,
                                 decimal debit,
                                 decimal credit,
                                 string? due       = null,
                                 bool    cancelled = false
    ) {
        return new LedgerEntry {
                                   Date        = DateOnly.Parse(date),
                                   DueDate     = due is null ? null : DateOnly.Parse(due),
                                   Debit       = debit,
                                   Credit      = credit,
                                   Created     = BaseTime.AddSeconds(Interlocked.Increment(ref _tick)),
                                   CancelledAt = cancelled ? DateTimeOffset.UtcNow : null
                               };
    }

    private static List<DueLineRef> Lines(IEnumerable<LedgerEntry> entries) {
        return entries.Select(e => new DueLineRef(LedgerDueLine.ForEntry(e), e.Date, e.Created, e.IsCancelled))
            .ToList();
    }

    private static List<LedgerAllocation> ToAllocations(IEnumerable<AllocationMatch> matches,
                                                        AllocationKind               kind = AllocationKind.Auto
    ) {
        return matches.Select(m => new LedgerAllocation {
                                                            DebitLineId  = m.DebitLineId,
                                                            CreditLineId = m.CreditLineId,
                                                            Amount       = m.Amount,
                                                            Kind         = kind
                                                        }).ToList();
    }

    private static IReadOnlyList<OpenItem> ViaStoredAllocations(IReadOnlyList<DueLineRef> lines, DateOnly today) {
        var allocations = ToAllocations(DueMatching.AutoAllocations(lines, []));
        return DueMatching.OpenItems(lines, allocations, today);
    }

    private static object Visible(OpenItem i) {
        return (i.EntryId, i.Date, i.DueDate, i.Direction, i.Remaining, i.DaysOverdue);
    }

    private static IEnumerable<TestCaseData> Scenarios() {
        yield return new TestCaseData((object)new[] {
                                                        E("2025-12-01", 100, 0, "2026-01-01"),
                                                        E("2026-01-01", 200, 0, "2026-02-01"),
                                                        E("2026-01-10", 0,   150)
                                                    }, Today).SetName("CreditClosesOldestDebitFirst");
        yield return new TestCaseData((object)new[] {
                                                        E("2025-12-01", 100, 0, "2026-01-01"),
                                                        E("2026-01-01", 200, 0, "2026-02-01"),
                                                        E("2026-01-10", 0,   150)
                                                    }, DateOnly.Parse("2026-02-10")).SetName("PartialAndOverdueDays");
        yield return new TestCaseData((object)new[] { E("2026-02-01", 0, 500, "2026-03-01"), E("2026-02-05", 200, 0) },
                                      Today).SetName("SupplierSideProducesPayable");
        yield return new TestCaseData((object)new[] { E("2026-01-01", 100, 0), E("2026-01-02", 0, 100, cancelled: true) },
                                      Today).SetName("IgnoresCancelled");
        yield return new TestCaseData((object)new[] { E("2026-01-01", 100, 0), E("2026-01-02", 0, 100) },
                                      Today).SetName("FullyMatched");
    }

    [TestCaseSource(nameof(Scenarios))]
    public void AutoAllocator_SameResultAsReadTimeFifo(LedgerEntry[] entries, DateOnly today) {
        var expected = DueMatching.OpenItems(entries, today);

        var lines  = Lines(entries);
        var actual = ViaStoredAllocations(lines, today);

        actual.Select(Visible).ToList().ShouldBe(expected.Select(Visible).ToList());
        foreach (var item in actual) {
            item.Sequence.ShouldBe(1);
            item.DueLineId.ShouldBe(lines.Single(l => l.Line.EntryId == item.EntryId).Line.Id);
        }
    }

    [Test]
    public void DueLine_ForEntry_UsesAmountDirectionAndDueDate() {
        var debit  = E("2026-01-01", 100, 0, "2026-02-01");
        var credit = E("2026-01-05", 0,   40);

        var d = LedgerDueLine.ForEntry(debit);
        var c = LedgerDueLine.ForEntry(credit);

        d.EntryId.ShouldBe(debit.Id);
        d.Amount.ShouldBe(100m);
        d.Direction.ShouldBe(DueDirection.Receivable);
        d.DueDate.ShouldBe(DateOnly.Parse("2026-02-01"));
        d.Sequence.ShouldBe(1);
        c.Amount.ShouldBe(40m);
        c.Direction.ShouldBe(DueDirection.Payable);
        c.DueDate.ShouldBe(DateOnly.Parse("2026-01-05"));
    }

    [Test]
    public void Manual_ConsumedFirst_ThenFifoFillsRest() {
        var older  = E("2026-01-01", 100, 0, "2026-01-05");
        var newer  = E("2026-01-02", 100, 0, "2026-01-10");
        var credit = E("2026-01-03", 0,   150);
        var lines  = Lines([older, newer, credit]);
        var manual = new LedgerAllocation {
                                              DebitLineId  = lines[1].Line.Id,
                                              CreditLineId = lines[2].Line.Id,
                                              Amount       = 100m,
                                              Kind         = AllocationKind.Manual
                                          };

        var auto = DueMatching.AutoAllocations(lines, [manual]);

        auto.ShouldHaveSingleItem().ShouldBe(new AllocationMatch(lines[0].Line.Id, lines[2].Line.Id, 50m));
        var open = DueMatching.OpenItems(lines, [manual, ..ToAllocations(auto)], Today);
        open.ShouldHaveSingleItem().EntryId.ShouldBe(older.Id);
        open[0].Remaining.ShouldBe(50m);
    }

    [Test]
    public void OpenItems_IgnoresAllocationsTouchingCancelledLines() {
        var debit     = E("2026-01-01", 100, 0);
        var cancelled = E("2026-01-02", 0,   100, cancelled: true);
        var lines     = Lines([debit, cancelled]);
        var stale = new LedgerAllocation
                    { DebitLineId = lines[0].Line.Id, CreditLineId = lines[1].Line.Id, Amount = 100m };

        DueMatching.AutoAllocations(lines, [stale]).ShouldBeEmpty();
        DueMatching.OpenItems(lines, [stale], Today).ShouldHaveSingleItem().Remaining.ShouldBe(100m);
    }
}
