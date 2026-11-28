using ERP.Domain.Pos;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pos;

public class ShiftMathTests {
    [Test]
    public void ShiftMath_ExpectedCash() {
        // açılış 500 + net nakit satış 147,50 - nakit iade 20 + giriş 100 - çıkış 50
        ShiftMath.ExpectedCash(500m, 147.50m, 20m, 100m, 50m).ShouldBe(677.50m);
        ShiftMath.ExpectedCash(0m, 0m, 0m, 0m, 0m).ShouldBe(0m);
    }
}
