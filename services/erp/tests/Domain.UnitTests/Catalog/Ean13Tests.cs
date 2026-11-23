using ERP.Domain.Catalog;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Catalog;

public class Ean13Tests {
    [TestCase("400638133393", 1)]
    [TestCase("200000000001", 5)]
    public void Ean13_CheckDigit_KnownVectors(string twelve, int expected) {
        Ean13.CheckDigit(twelve).ShouldBe(expected);
    }

    [TestCase("4006381333931", true)]
    [TestCase("4006381333932", false)]
    [TestCase("400638133393", false)]
    [TestCase("40063813339A1", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void Ean13_IsValid_RejectsWrongCheckDigitAndNonDigits(string? code, bool expected) {
        Ean13.IsValid(code!).ShouldBe(expected);
    }

    [Test]
    public void Ean13_Build_PadsSequenceTo5() {
        var code = Ean13.Build("2000000", 1);
        code.ShouldBe("2000000" + "00001" + "5");
        code.Length.ShouldBe(13);
        Ean13.IsValid(code).ShouldBeTrue();
    }
}
