using ERP.Domain.Inventory;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Inventory;

public class FifoCostingTests {
    static CostLayer Layer(decimal remaining, decimal cost) {
        return new CostLayer { OriginalQty = remaining, RemainingQty = remaining, UnitCost = cost };
    }

    [Test]
    public void Fifo_ConsumesOldestLayersAcrossTwo() {
        var l1 = Layer(5m, 100m);
        var l2 = Layer(10m, 120m);

        var (total, consumed) = FifoCosting.Consume(new[] { l1, l2 }, 8m, 0m);

        total.ShouldBe(5m * 100m + 3m * 120m);
        consumed.Count.ShouldBe(2);
        consumed[0].ShouldBe((l1.Id, 5m));
        consumed[1].ShouldBe((l2.Id, 3m));
    }

    [Test]
    public void Fifo_ShortfallUsesFallbackCost() {
        var l1 = Layer(2m, 100m);

        var (total, consumed) = FifoCosting.Consume(new[] { l1 }, 5m, 90m);

        total.ShouldBe(2m * 100m + 3m * 90m);
        consumed.Count.ShouldBe(1);
        consumed[0].ShouldBe((l1.Id, 2m));
    }
}
