using ERP.Application.Pos.Sales;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Pos;

public class PosLinePricingTests {
    private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    [Test]
    public void VatIncluded_TotalEqualsShelfPrice_ForEveryPriceAndRate() {
        foreach (var rate in new[] { 1, 10, 20 })
        foreach (var quantity in new[] { 1m, 2m, 3m, 0.5m, 1.235m })
            for (var kurus = 1; kurus <= 30000; kurus++) {
                var price = kurus / 100m;
                var line  = PosLinePricing.VatIncluded(price, quantity, rate, 0m, null);
                var shelf = R2(quantity * price);
                var label = $"price {price} x {quantity} @ %{rate}";

                line.Total.ShouldBe(shelf, label);
                (line.Discount + line.CampaignDiscount).ShouldBe(0m, label);
                (line.Net + line.Vat).ShouldBe(shelf, label);
                Math.Abs(line.Vat - R2(line.Net * rate / 100m)).ShouldBeLessThanOrEqualTo(0.01m, label);

                // Fatura satırı (birim fiyat x miktar - iskonto) aynı net tutarı verir.
                (R2(quantity * line.UnitNet) - line.Discount - line.CampaignDiscount).ShouldBe(line.Net, label);
            }
    }

    [Test]
    public void VatIncluded_DiscountsApplyOnTheGrossSide() {
        // 3 x 9,99 = 29,97; kampanya 5,00 (KDV dahil); elle %10 => (29,97 - 5,00) x 0,10 = 2,497 -> 2,50; toplam 22,47.
        var line = PosLinePricing.VatIncluded(9.99m, 3m, 20, 5m, 10m);
        line.ManualGross.ShouldBe(2.50m);
        (line.Discount + line.CampaignDiscount).ShouldBeGreaterThan(0m);
        line.CampaignDiscount.ShouldBe(4.17m);   // 5,00 / 1,20
        line.Total.ShouldBe(22.47m);
        (line.Net + line.Vat).ShouldBe(22.47m);
        (R2(3m * line.UnitNet) - line.Discount - line.CampaignDiscount).ShouldBe(line.Net);
    }
}
