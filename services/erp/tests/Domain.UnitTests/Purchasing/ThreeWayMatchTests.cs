using ERP.Domain.Purchasing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Purchasing;

public class ThreeWayMatchTests {
    [Test]
    public void ThreeWayMatch_PriceVarianceOverThreshold() {
        var over  = ThreeWayMatch.Evaluate(Guid.NewGuid(), 10, 10, 10, 100m, 100m, 103m, 2m);
        var under = ThreeWayMatch.Evaluate(Guid.NewGuid(), 10, 10, 10, 100m, 100m, 101m, 2m);
        over.PriceVariance.ShouldBeTrue();
        under.PriceVariance.ShouldBeFalse();
        over.QtyMismatch.ShouldBeFalse();
    }

    [Test]
    public void ThreeWayMatch_QtyMismatch() {
        var row = ThreeWayMatch.Evaluate(Guid.NewGuid(), 10, 8, 10, 100m, 100m, 100m, 2m);
        row.QtyMismatch.ShouldBeTrue();
        row.PriceVariance.ShouldBeFalse();
    }
}
