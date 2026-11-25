using ERP.Domain.Sales;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Sales;

public class PlanConsumerTests {
    private static readonly DateOnly OrderDate = new(2026, 12, 1);

    private static List<PlanRow> FourRows() => new() {
        new(1, new DateOnly(2027, 1, 1), 100m), new(2, new DateOnly(2027, 2, 1), 100m),
        new(3, new DateOnly(2027, 3, 1), 100m), new(4, new DateOnly(2027, 4, 1), 100m)
    };

    [Test]
    public void Consume_OneAndHalfRows() {
        var first = PlanConsumer.Consume(FourRows(), 0m, 150m, OrderDate);
        first.Schedule.Select(r => (r.Sequence, r.Amount)).ShouldBe(new[] { (1, 100m), (2, 50m) });
        first.Remaining.Select(r => (r.Sequence, r.Amount)).ShouldBe(new[] { (2, 50m), (3, 100m), (4, 100m) });

        var second = PlanConsumer.Consume(first.Remaining, 0m, 250m, OrderDate);
        second.Schedule.Select(r => (r.Sequence, r.Amount)).ShouldBe(new[] { (2, 50m), (3, 100m), (4, 100m) });
        second.Remaining.ShouldBeEmpty();
    }

    [Test]
    public void Consume_ExactlyAllRows() {
        var r = PlanConsumer.Consume(FourRows(), 0m, 400m, OrderDate);
        r.Schedule.Count.ShouldBe(4);
        r.Remaining.ShouldBeEmpty();
        r.DownPaymentRemaining.ShouldBe(0m);
    }

    [Test]
    public void Consume_FirstInvoiceCarriesDownPayment() {
        var rows = InstallmentSplitter.Equal(7360m, 2453.33m, 2, new DateOnly(2027, 1, 15));
        var r    = PlanConsumer.Consume(rows, 2453.33m, 7360m, OrderDate);
        r.Schedule.Select(x => (x.Sequence, x.Amount)).ShouldBe(new[] { (0, 2453.33m), (1, 2453.33m), (2, 2453.34m) });
        r.Schedule[0].DueDate.ShouldBe(OrderDate);
        r.Remaining.ShouldBeEmpty();
        r.DownPaymentRemaining.ShouldBe(0m);
    }

    [Test]
    public void Consume_PartialFirstInvoiceSmallerThanDownPayment() {
        var r = PlanConsumer.Consume(FourRows(), 300m, 120m, OrderDate);
        r.Schedule.Select(x => (x.Sequence, x.Amount)).ShouldBe(new[] { (0, 120m) });
        r.DownPaymentRemaining.ShouldBe(180m);
        r.Remaining.Count.ShouldBe(4);
    }

    [Test]
    public void Consume_PayableExceedsDownPaymentPlusRemaining_Throws() {
        Should.Throw<InvalidOperationException>(() => PlanConsumer.Consume(FourRows(), 50m, 451m, OrderDate));
    }
}
