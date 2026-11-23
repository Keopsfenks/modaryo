using ERP.Domain.Pricing;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pricing;

public class PriceResolverTests {
    static readonly DateOnly Today = new(2026, 11, 24);

    static PriceCandidate C(
        PriceSource source, decimal price, bool variant = false, DateOnly? from = null, DateOnly? to = null,
        params PriceTier[] tiers) {
        IReadOnlyList<PriceTier> t = tiers.Length > 0 ? tiers : [new PriceTier(1, price)];
        return new PriceCandidate(source, Guid.NewGuid(), variant, "TRY", false, t, from, to);
    }

    [Test]
    public void Resolver_FallsThroughPerItemAndSkipsExpired() {
        // X: müşteriye özel fiyat dün bitti, grup listesinde X yok -> varsayılan liste.
        var x = new[] {
            C(PriceSource.CustomerPrice, 70m, to: Today.AddDays(-1)),
            C(PriceSource.DefaultList, 100m),
            C(PriceSource.CardDefault, 120m)
        };
        var quoteX = PriceResolver.Resolve(x, PriceResolver.SalePriority, 1m, Today);
        quoteX.ShouldNotBeNull();
        quoteX.Source.ShouldBe(PriceSource.DefaultList);
        quoteX.UnitPrice.ShouldBe(100m);

        // Y: grup listesinde var.
        var y = new[] { C(PriceSource.GroupList, 80m), C(PriceSource.DefaultList, 100m) };
        var quoteY = PriceResolver.Resolve(y, PriceResolver.SalePriority, 1m, Today);
        quoteY.ShouldNotBeNull();
        quoteY.Source.ShouldBe(PriceSource.GroupList);
        quoteY.UnitPrice.ShouldBe(80m);

        // Henüz başlamamış fiyat da atlanır.
        var future = new[] { C(PriceSource.GroupList, 80m, from: Today.AddDays(1)), C(PriceSource.CardDefault, 120m) };
        PriceResolver.Resolve(future, PriceResolver.SalePriority, 1m, Today)!.Source.ShouldBe(PriceSource.CardDefault);
    }

    [Test]
    public void Resolver_VariantRowBeatsProductRowSameLevel() {
        var rows = new[] {
            C(PriceSource.DefaultList, 100m),
            C(PriceSource.DefaultList, 90m, true),
            C(PriceSource.CardDefault, 50m, true)
        };
        var quote = PriceResolver.Resolve(rows, PriceResolver.SalePriority, 1m, Today);
        quote!.UnitPrice.ShouldBe(90m);
        quote.Source.ShouldBe(PriceSource.DefaultList);
    }

    [Test]
    public void Resolver_TierBoundaryInclusive() {
        var tiers = new[] { new PriceTier(1, 100m), new PriceTier(10, 90m) };
        var rows  = new[] { C(PriceSource.DefaultList, 0m, tiers: tiers) };

        PriceResolver.Resolve(rows, PriceResolver.SalePriority, 10m, Today)!.UnitPrice.ShouldBe(90m);
        PriceResolver.Resolve(rows, PriceResolver.SalePriority, 10m, Today)!.TierMinQty.ShouldBe(10m);
        PriceResolver.Resolve(rows, PriceResolver.SalePriority, 9.99m, Today)!.UnitPrice.ShouldBe(100m);
    }

    [Test]
    public void Resolver_NoCandidate_ReturnsNull() {
        PriceResolver.Resolve([], PriceResolver.SalePriority, 1m, Today).ShouldBeNull();

        // Yalnızca öncelik listesinde olmayan kaynak.
        var rows = new[] { C(PriceSource.SupplierList, 10m) };
        PriceResolver.Resolve(rows, PriceResolver.SalePriority, 1m, Today).ShouldBeNull();
    }

    [Test]
    public void Resolver_PurchasePriority() {
        var rows = new[] {
            C(PriceSource.CardDefault, 60m),
            C(PriceSource.DefaultList, 50m),
            C(PriceSource.SupplierList, 40m)
        };
        PriceResolver.Resolve(rows, PriceResolver.PurchasePriority, 1m, Today)!.Source.ShouldBe(PriceSource.SupplierList);

        PriceResolver.Resolve(rows.Skip(0).Take(2), PriceResolver.PurchasePriority, 1m, Today)!
            .Source.ShouldBe(PriceSource.DefaultList);

        PriceResolver.SalePriority.ShouldBe(
            [PriceSource.CustomerPrice, PriceSource.GroupList, PriceSource.DefaultList, PriceSource.CardDefault]);
        PriceResolver.PurchasePriority.ShouldBe(
            [PriceSource.SupplierList, PriceSource.DefaultList, PriceSource.CardDefault]);
    }

    [Test]
    public void TiersValid_RejectsNonIncreasingAndFirstNotOne() {
        PriceResolver.TiersValid([new PriceTier(1, 10m), new PriceTier(5, 9m)]).ShouldBeTrue();
        PriceResolver.TiersValid([new PriceTier(1, 10m)]).ShouldBeTrue();
        PriceResolver.TiersValid([]).ShouldBeFalse();
        PriceResolver.TiersValid([new PriceTier(2, 10m)]).ShouldBeFalse();
        PriceResolver.TiersValid([new PriceTier(1, 10m), new PriceTier(1, 9m)]).ShouldBeFalse();
        PriceResolver.TiersValid([new PriceTier(1, 10m), new PriceTier(5, 9m), new PriceTier(3, 8m)]).ShouldBeFalse();
        PriceResolver.TiersValid([new PriceTier(1, -1m)]).ShouldBeFalse();
    }
}
