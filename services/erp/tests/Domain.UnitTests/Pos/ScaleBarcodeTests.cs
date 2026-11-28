using ERP.Domain.Catalog;
using ERP.Domain.Pos;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pos;

public class ScaleBarcodeTests {
    private static readonly ScaleFormat[] Formats = [
        new("27", 5, 5, ScaleValueKind.WeightGrams),
        new("28", 5, 5, ScaleValueKind.PriceKurus)
    ];

    [Test]
    public void ScaleBarcode_WeightAndPriceKinds() {
        // 27 | 12345 | 01234 g | kontrol hanesi
        var weight = ScaleBarcode.TryParse(Ean13.Build("2712345", 1234), Formats);
        weight.ShouldNotBeNull();
        weight.ItemCode.ShouldBe("12345");
        weight.WeightKg.ShouldBe(1.234m);
        weight.PriceTry.ShouldBeNull();

        // 28 | 12345 | 00750 kuruş = 7,50 TL
        var price = ScaleBarcode.TryParse(Ean13.Build("2812345", 750), Formats);
        price.ShouldNotBeNull();
        price.ItemCode.ShouldBe("12345");
        price.PriceTry.ShouldBe(7.5m);
        price.WeightKg.ShouldBeNull();
    }

    [Test]
    public void ScaleBarcode_BadCheckDigit_Null() {
        var valid = Ean13.Build("2712345", 1234);
        var bad   = valid[..12] + ((valid[12] - '0' + 1) % 10);
        ScaleBarcode.TryParse(bad, Formats).ShouldBeNull();
    }

    [Test]
    public void ScaleBarcode_UnknownPrefix_Null() {
        ScaleBarcode.TryParse(Ean13.Build("2912345", 1234), Formats).ShouldBeNull();
        ScaleBarcode.TryParse("12345", Formats).ShouldBeNull();
        ScaleBarcode.TryParse(Ean13.Build("2712345", 1234), []).ShouldBeNull();
    }
}
