using ERP.Domain.Pricing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pricing;

public class PriceRoundingTests {
    [TestCase("101.23", "99.90")]
    [TestCase("104.89", "99.90")]
    [TestCase("104.90", "109.90")]
    [TestCase("109.90", "109.90")]
    [TestCase("3.00", "9.90")]
    public void Rounding_NineEnding(string input, string expected) {
        PriceRounding.Apply(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), RoundingRule.NineEnding)
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestCase("10.02", "10.00")]
    [TestCase("10.03", "10.05")]
    [TestCase("10.025", "10.05")]
    public void Rounding_FiveCents(string input, string expected) {
        PriceRounding.Apply(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), RoundingRule.FiveCents)
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public void Rounding_OtherRules() {
        PriceRounding.Apply(12.3456m, RoundingRule.None).ShouldBe(12.3456m);
        PriceRounding.Apply(12.3456m, RoundingRule.Cent).ShouldBe(12.35m);
        PriceRounding.Apply(12.34m, RoundingRule.TenCents).ShouldBe(12.30m);
        PriceRounding.Apply(12.75m, RoundingRule.FiftyCents).ShouldBe(13.00m);
        PriceRounding.Apply(12.49m, RoundingRule.One).ShouldBe(12m);
    }
}
