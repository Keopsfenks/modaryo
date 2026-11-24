using ERP.Application.Catalog.Items;
using ERP.Application.Catalog.Products;
using ERP.Application.Common.Interfaces;

namespace ERP.Application.Pricing;

/// <summary>
///     Alış fiyatı görünürlüğünün tek kapısı: fiyat.alis izni olmayan kullanıcıya alış fiyatı hiçbir DTO, dışa aktarma
///     ya da arama sonucunda gitmez. Alış fiyatı taşıyan her yeni çıktı tipi buraya bir Mask ekler.
/// </summary>
public static class PurchaseVisibility {
    public static bool CanSee(ICurrentSession session) {
        return session.HasPermission(PricingPermissions.Purchase);
    }

    public static ProductDto Mask(ProductDto dto, bool canSee) {
        return canSee ? dto : dto with {
            DefaultPurchasePrice = null,
            Variants = dto.Variants.Select(v => v with { PurchasePriceOverride = null }).ToList()
        };
    }

    public static SellableItem Mask(SellableItem item, bool canSee) {
        return canSee ? item : item with { PurchasePrice = null };
    }

    public static BarcodeHit Mask(BarcodeHit hit, bool canSee) {
        return canSee ? hit : hit with { Item = Mask(hit.Item, false) };
    }

    /// <summary>Yazma tarafı: izni olmayan kullanıcının gönderdiği alış fiyatları yok sayılır (mevcut değer korunur, yenide boş).</summary>
    public static ProductInput StripPurchase(ProductInput input, Domain.Catalog.Product? existing) {
        return input with {
            DefaultPurchasePrice = existing?.DefaultPurchasePrice,
            Variants = input.Variants?.Select(v => v with { PurchasePriceOverride = null }).ToList()
        };
    }
}
