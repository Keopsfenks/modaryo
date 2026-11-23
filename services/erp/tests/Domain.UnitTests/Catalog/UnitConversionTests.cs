using ERP.Domain.Catalog;
using ERP.Domain.Common;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Catalog;

public class UnitConversionTests {
    [Test]
    public void UnitConversion_KoliOf12_RoundTrips() {
        UnitConversion.ToBase(3m, 12m).ShouldBe(36m);
        UnitConversion.FromBase(36m, 12m).ShouldBe(3m);
        UnitConversion.FromBase(UnitConversion.ToBase(2.5m, 12m), 12m).ShouldBe(2.5m);
    }

    [TestCase("IŞIK LAMBA", "işik lamba")]
    [TestCase("  Işık ", "işik")]
    [TestCase("İSTANBUL", "istanbul")]
    public void TurkishText_Fold_DottedAndDotlessI(string input, string expected) {
        TurkishText.Fold(input).ShouldBe(expected);
    }
}
