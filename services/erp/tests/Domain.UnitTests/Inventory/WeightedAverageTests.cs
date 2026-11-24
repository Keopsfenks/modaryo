using ERP.Domain.Inventory;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Inventory;

public class WeightedAverageTests {
    [Test]
    public void WeightedAverage_TwoReceiptsThenIssue() {
        var (q1, a1) = WeightedAverage.ApplyInbound(0m, 0m, 10m, 100m);
        q1.ShouldBe(10m);
        a1.ShouldBe(100m);

        var (q2, a2) = WeightedAverage.ApplyInbound(q1, a1, 10m, 120m);
        q2.ShouldBe(20m);
        a2.ShouldBe(110m);

        WeightedAverage.IssueCost(a2, 5m).ShouldBe(550m);
    }

    [Test]
    public void WeightedAverage_InboundAfterNegative_UsesInboundCost() {
        var (q, a) = WeightedAverage.ApplyInbound(-3m, 90m, 10m, 120m);
        q.ShouldBe(7m);
        a.ShouldBe(120m);
    }
}
