namespace ERP.Application.Common.Interfaces;

/// <summary>
///     Bir ürünün ya da varyantın başka bir modülde (stok hareketi, fatura satırı vb.) kullanılıp kullanılmadığını
///     söyler. Kalem verisi taşıyan her modül kendi uygulamasını kaydeder; P1'de hiçbiri kayıtlı değildir.
/// </summary>
public interface IItemUsageProvider {
    Task<bool> ProductInUseAsync(Guid productId, CancellationToken ct);

    Task<bool> VariantInUseAsync(Guid variantId, CancellationToken ct);
}

/// <summary>Kayıtlı tüm sağlayıcıların bileşimi: herhangi biri kullanımda diyorsa kullanımdadır. Kayıt yoksa false.</summary>
public sealed class ItemUsage(IEnumerable<IItemUsageProvider> providers) {
    private readonly IItemUsageProvider[] _providers = providers.ToArray();

    public async Task<bool> ProductInUseAsync(Guid productId, CancellationToken ct) {
        foreach (var provider in _providers)
            if (await provider.ProductInUseAsync(productId, ct))
                return true;

        return false;
    }

    public async Task<bool> VariantInUseAsync(Guid variantId, CancellationToken ct) {
        foreach (var provider in _providers)
            if (await provider.VariantInUseAsync(variantId, ct))
                return true;

        return false;
    }
}
