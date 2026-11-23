using ERP.Domain.Catalog;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Catalog;

public class VariantGeneratorTests {
    [Test]
    public void VariantGenerator_TwoAttributes_Cartesian() {
        var attrs = new List<VariantAttribute> {
            new() { Name = "Beden", Values = ["S", "M", "L"], Sort = 2 },
            new() { Name = "Renk", Values = ["Kırmızı", "Mavi"], Sort = 1 }
        };

        var result = VariantGenerator.Combinations(attrs);

        result.Count.ShouldBe(6);
        result[0]["Renk"].ShouldBe("Kırmızı");
        result[0]["Beden"].ShouldBe("S");
        result[1]["Beden"].ShouldBe("M");
        result[2]["Beden"].ShouldBe("L");
        result[3]["Renk"].ShouldBe("Mavi");
        result[3]["Beden"].ShouldBe("S");
        result[5]["Renk"].ShouldBe("Mavi");
        result[5]["Beden"].ShouldBe("L");
    }

    [Test]
    public void VariantGenerator_NoAttributes_Empty() {
        VariantGenerator.Combinations(new List<VariantAttribute>()).ShouldBeEmpty();
    }
}
