namespace ERP.Domain.Purchasing;

/// <summary>Kesilmiş alış faturalarından, ürün ve birim başına 12 aylık TL birim fiyat istatistiği (okuma modeli).</summary>
public sealed record PurchasePriceStat(
    Guid ProductId,
    Guid? VariantId,
    string Unit,
    decimal LastUnitPriceTl,
    decimal MinUnitPriceTl,
    decimal MaxUnitPriceTl,
    DateOnly LastPurchaseDate) {

    public static PurchasePriceStat? From(Guid productId, Guid? variantId, string unit,
        IEnumerable<(DateOnly Date, decimal UnitPriceTl)> purchases, DateOnly today) {
        var window = purchases.Where(p => p.Date > today.AddMonths(-12) && p.Date <= today)
            .OrderBy(p => p.Date).ToList();
        if (window.Count == 0) return null;
        var last = window[^1];
        return new PurchasePriceStat(productId, variantId, unit, last.UnitPriceTl,
            window.Min(p => p.UnitPriceTl), window.Max(p => p.UnitPriceTl), last.Date);
    }
}
