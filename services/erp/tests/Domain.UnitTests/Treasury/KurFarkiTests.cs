using ERP.Domain.Treasury;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Treasury;

public class KurFarkiTests {
    [Test]
    public void KurFarki_PositiveAndNegative() {
        KurFarki.Compute(1000m, 32.00m, 32.50m).ShouldBe(500m);
        KurFarki.Compute(1000m, 32.50m, 32.00m).ShouldBe(-500m);
        KurFarki.Compute(1000m, 32m, 32m).ShouldBe(0m);
        KurFarki.Compute(33.33m, 32.00m, 32.50m).ShouldBe(16.67m);
    }
}
