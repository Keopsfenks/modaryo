namespace ERP.Domain.Pricing;

public sealed record PriceCandidate(
    PriceSource Source, Guid? SourceId, bool VariantSpecific, string Currency, bool VatIncluded,
    IReadOnlyList<PriceTier> Tiers, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record PriceQuote(
    decimal UnitPrice, string Currency, bool VatIncluded, PriceSource Source, Guid? SourceId, decimal TierMinQty);

public static class PriceResolver {
    public static IReadOnlyList<PriceSource> SalePriority { get; } =
        [PriceSource.CustomerPrice, PriceSource.GroupList, PriceSource.DefaultList, PriceSource.CardDefault];

    public static IReadOnlyList<PriceSource> PurchasePriority { get; } =
        [PriceSource.SupplierList, PriceSource.DefaultList, PriceSource.CardDefault];

    /// <summary>
    ///     Adaylar zaten kalem+birime göre süzülmüş olmalı. Öncelik sırasındaki ilk, tarihte geçerli ve
    ///     miktara uyan kademesi olan aday kazanır; aynı seviyede varyanta özel satır önce gelir.
    /// </summary>
    public static PriceQuote? Resolve(
        IEnumerable<PriceCandidate> candidates, IReadOnlyList<PriceSource> priority, decimal quantity, DateOnly date) {
        var list = candidates.Where(c => IsEffective(c, date)).ToList();

        foreach (var source in priority) {
            foreach (var candidate in list.Where(c => c.Source == source).OrderByDescending(c => c.VariantSpecific)) {
                var tier = candidate.Tiers
                    .Where(t => t.MinQty <= quantity)
                    .OrderByDescending(t => t.MinQty)
                    .FirstOrDefault();
                if (tier is null) continue;

                return new PriceQuote(
                    tier.Price, candidate.Currency, candidate.VatIncluded, candidate.Source, candidate.SourceId, tier.MinQty);
            }
        }

        return null;
    }

    /// <summary>Boş olmamalı, ilk MinQty 1, MinQty kesin artan, fiyatlar negatif olmamalı.</summary>
    public static bool TiersValid(IReadOnlyList<PriceTier> tiers) {
        if (tiers.Count == 0 || tiers[0].MinQty != 1m) return false;
        for (var i = 0; i < tiers.Count; i++) {
            if (tiers[i].Price < 0m) return false;
            if (i > 0 && tiers[i].MinQty <= tiers[i - 1].MinQty) return false;
        }

        return true;
    }

    static bool IsEffective(PriceCandidate c, DateOnly date) {
        return (c.ValidFrom is null || c.ValidFrom <= date) && (c.ValidTo is null || date <= c.ValidTo);
    }
}
