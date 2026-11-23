using ERP.Domain.Pricing;

namespace ERP.Application.Pricing;

/// <summary>
///     Bir liste için bir kalemin etkin kademeleri: açık satır varsa o, yoksa taban listeden (yüzde ayarı ve
///     yuvarlamayla) türetilen değer. Zincir en fazla 3 derinliktedir; döngüye karşı ek bir sınır tutulur.
/// </summary>
public sealed class PriceGraph {
    private readonly IReadOnlyDictionary<Guid, PriceList>                       _lists;
    private readonly ILookup<(Guid, Guid, Guid?, string), PriceListItem>        _rows;
    private readonly RoundingRule                                               _rounding;

    public PriceGraph(IReadOnlyDictionary<Guid, PriceList> lists, IEnumerable<PriceListItem> items, RoundingRule rounding) {
        _lists    = lists;
        _rounding = rounding;
        _rows     = items.ToLookup(i => (i.PriceListId, i.ProductId, i.VariantId, i.Unit));
    }

    public (IReadOnlyList<PriceTier> Tiers, bool Derived, PriceListItem? Row)? TiersFor(
        Guid listId, Guid productId, Guid? variantId, string unit, int depth = 0) {
        if (depth > 6) return null;

        var row = _rows[(listId, productId, variantId, unit)].FirstOrDefault();
        if (row is not null) return (row.Tiers, false, row);

        if (!_lists.TryGetValue(listId, out var list) || list.BaseListId is not { } baseId
                                                      || list.AdjustmentPercent is not { } percent)
            return null;

        var inner = TiersFor(baseId, productId, variantId, unit, depth + 1);
        if (inner is null) return null;

        var factor = 1m + percent / 100m;
        var tiers = inner.Value.Tiers
            .Select(t => new PriceTier(t.MinQty, PriceRounding.Apply(Math.Round(t.Price * factor, 4), _rounding))).ToList();
        return (tiers, true, null);
    }

    /// <summary>Listeden başlayıp taban listelere giden zincirin liste kimlikleri (kendisi dahil).</summary>
    public static List<Guid> Chain(IReadOnlyDictionary<Guid, PriceList> lists, Guid listId) {
        var chain = new List<Guid>();
        Guid? current = listId;
        while (current is { } id && !chain.Contains(id) && lists.TryGetValue(id, out var list)) {
            chain.Add(id);
            current = list.BaseListId;
        }

        return chain;
    }
}
