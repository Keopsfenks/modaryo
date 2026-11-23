using ERP.Application.Catalog.Products;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Items;

/// <summary>Satış, alış ve stok modüllerinin gördüğü tek kalem biçimi: varyantsız kart ya da bir varyant.</summary>
public sealed record SellableItem(
    Guid                   ProductId,
    Guid?                  VariantId,
    ProductKind            Kind,
    string                 Code,
    string                 Name,
    string                 BaseUnit,
    int                    VatRate,
    bool                   SoldByMeasure,
    string?                MeasureUnit,
    bool                   TracksSerialLot,
    Money4?                SalePrice,
    Money4?                PurchasePrice,
    IReadOnlyList<UnitDto> Units,
    bool                   IsActive
) {
    /// <summary>variant null ise kartın kendisi; verilirse varyant (fiyat geçersiz kılması karttan önce gelir).</summary>
    public static SellableItem From(Product product, ProductVariant? variant) {
        var units = product.Units.OrderBy(u => u.Unit, StringComparer.Ordinal)
                        .Select(u => new UnitDto(u.Id, u.Unit, u.Factor, u.Barcode)).ToList();
        if (variant is null)
            return new SellableItem(product.Id, null, product.Kind, product.Code, product.Name, product.BaseUnit,
                                    product.VatRate, product.SoldByMeasure, product.MeasureUnit, product.TracksSerialLot,
                                    product.DefaultSalePrice, product.DefaultPurchasePrice, units, product.IsActive);

        var ordered = product.Attributes.OrderBy(a => a.Sort).Select(a => variant.Values.GetValueOrDefault(a.Name))
            .Where(v => v is not null).Select(v => v!).ToList();
        if (ordered.Count == 0) ordered = variant.Values.Values.ToList();
        var name = ordered.Count == 0 ? product.Name : $"{product.Name} — {string.Join(" / ", ordered)}";

        return new SellableItem(product.Id, variant.Id, product.Kind, variant.Code, name, product.BaseUnit,
                                product.VatRate, product.SoldByMeasure, product.MeasureUnit, product.TracksSerialLot,
                                variant.SalePriceOverride ?? product.DefaultSalePrice,
                                variant.PurchasePriceOverride ?? product.DefaultPurchasePrice, units,
                                product.IsActive && variant.IsActive);
    }
}

/// <summary>Barkodun bulduğu kalem; birim barkoduysa o birim ve çevrim katsayısı, değilse temel birim ve 1.</summary>
public sealed record BarcodeHit(SellableItem Item, string Unit, decimal Factor);
