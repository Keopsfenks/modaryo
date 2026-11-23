using ERP.Domain.Pricing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pricing;

public class CampaignEvaluatorTests {
    static readonly Guid Product = Guid.NewGuid();

    // 2026-11-24 Salı 14:00
    static readonly DateTime Now = new(2026, 11, 24, 14, 0, 0);

    static CampaignLine Line(decimal qty, decimal price = 100m) => new(Product, null, null, qty, price);

    static Campaign BuyXPayY(int x, int y) => new() {
        Name = $"{x} al {y} öde", Kind = CampaignKind.BuyXPayY, BuyQty = x, PayQty = y, ProductIds = [Product]
    };

    static Campaign Percent(decimal p) => new() {
        Name = $"%{p}", Kind = CampaignKind.PercentOff, Percent = p, ProductIds = [Product]
    };

    [Test]
    public void Campaign_BuyThreePayTwo() {
        var c = BuyXPayY(3, 2);
        CampaignEvaluator.Best(Line(3), [c], Now)!.DiscountAmount.ShouldBe(100m);
        CampaignEvaluator.Best(Line(7), [c], Now)!.DiscountAmount.ShouldBe(200m);
        CampaignEvaluator.Best(Line(2), [c], Now).ShouldBeNull();
    }

    [Test]
    public void Campaign_TimeWindowOutside_NoDiscount() {
        var c = Percent(10);
        c.Days     = [DayOfWeek.Saturday, DayOfWeek.Sunday];
        CampaignEvaluator.Best(Line(1), [c], Now).ShouldBeNull();

        c.Days     = [DayOfWeek.Tuesday];
        c.FromTime = new TimeOnly(18, 0);
        c.ToTime   = new TimeOnly(22, 0);
        CampaignEvaluator.Best(Line(1), [c], Now).ShouldBeNull();

        c.FromTime = new TimeOnly(9, 0);
        c.ToTime   = new TimeOnly(15, 0);
        CampaignEvaluator.Best(Line(1), [c], Now)!.DiscountAmount.ShouldBe(10m);

        c.ValidTo = new DateOnly(2026, 11, 23);
        CampaignEvaluator.Best(Line(1), [c], Now).ShouldBeNull();
    }

    [Test]
    public void Campaign_BestOfTwoNoStacking() {
        var a = Percent(10);
        var b = BuyXPayY(3, 2);
        // 3 adet: %10 = 30, 3 al 2 öde = 100 -> en iyi tek kampanya.
        var best = CampaignEvaluator.Best(Line(3), [a, b], Now)!;
        best.CampaignId.ShouldBe(b.Id);
        best.DiscountAmount.ShouldBe(100m);

        // 1 adet: yalnızca %10 geçerli.
        CampaignEvaluator.Best(Line(1), [a, b], Now)!.CampaignId.ShouldBe(a.Id);
    }

    [Test]
    public void Campaign_ScopeByCategoryAndVariant_AndInactive() {
        var cat     = Guid.NewGuid();
        var variant = Guid.NewGuid();
        var c       = new Campaign { Kind = CampaignKind.PercentOff, Percent = 50m, CategoryIds = [cat] };
        var line    = new CampaignLine(Guid.NewGuid(), variant, cat, 2, 10m);
        CampaignEvaluator.Best(line, [c], Now)!.DiscountAmount.ShouldBe(10m);

        c.CategoryIds = [];
        CampaignEvaluator.Best(line, [c], Now).ShouldBeNull();
        c.VariantIds = [variant];
        CampaignEvaluator.Best(line, [c], Now).ShouldNotBeNull();
        c.IsActive = false;
        CampaignEvaluator.Best(line, [c], Now).ShouldBeNull();
    }
}
