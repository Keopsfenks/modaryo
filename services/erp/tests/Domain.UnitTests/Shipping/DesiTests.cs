using ERP.Domain.Shipping;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Shipping;

public class DesiTests {
    [Test]
    public void Desi_RoundsUpToTenth() {
        Desi.Of(30, 30, 30).ShouldBe(9m);          // 27000 / 3000
        Desi.Of(10, 10, 10).ShouldBe(0.4m);        // 0.333.. -> 0.4
        Desi.Of(20, 20, 20).ShouldBe(2.7m);        // 2.666.. -> 2.7
        Desi.Of(31, 31, 31).ShouldBe(10m);       // 9.9303 -> 10.0
    }

    [Test]
    public void Desi_ZeroDimensionIsZero() {
        Desi.Of(0, 10, 10).ShouldBe(0m);
    }
}
