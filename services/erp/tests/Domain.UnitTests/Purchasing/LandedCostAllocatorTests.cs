using ERP.Domain.Purchasing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Purchasing;

public class LandedCostAllocatorTests {
    private static LandedTarget T(Guid id, decimal tl, decimal qty, decimal? kg = null) => new(id, tl, qty, kg);

    [Test]
    public void Allocate_ByAmount_SumsExactly() {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var r = LandedCostAllocator.Allocate(100m, AllocationKey.Amount, [T(a, 1, 1), T(b, 1, 1), T(c, 1, 1)]);
        r[a].ShouldBe(33.34m);
        r[b].ShouldBe(33.33m);
        r[c].ShouldBe(33.33m);
        r.Values.Sum().ShouldBe(100m);
    }

    [Test]
    public void Allocate_ByQuantity() {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var r = LandedCostAllocator.Allocate(90m, AllocationKey.Quantity, [T(a, 500, 10), T(b, 100, 20)]);
        r[a].ShouldBe(30m);
        r[b].ShouldBe(60m);
    }

    [Test]
    public void Allocate_ByWeight_UsesQtyTimesKg() {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var r = LandedCostAllocator.Allocate(100m, AllocationKey.Weight, [T(a, 1, 10, 1m), T(b, 1, 10, 4m)]);
        r[a].ShouldBe(20m);
        r[b].ShouldBe(80m);
    }

    [Test]
    public void Allocate_ByWeight_MissingThrows() {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var ex = Should.Throw<WeightMissingException>(() =>
            LandedCostAllocator.Allocate(100m, AllocationKey.Weight, [T(a, 1, 10, 1m), T(b, 1, 10)]));
        ex.ReceiptLineIds.ShouldBe([b]);
    }
}
