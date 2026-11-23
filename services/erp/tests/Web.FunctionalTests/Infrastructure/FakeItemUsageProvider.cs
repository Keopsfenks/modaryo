using System.Collections.Concurrent;
using ERP.Application.Common.Interfaces;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Testin "kullanımda" işaretlediği ürün ve varyantları döndürür.</summary>
public sealed class FakeItemUsageProvider : IItemUsageProvider {
    private readonly ConcurrentDictionary<Guid, bool> _products = new();
    private readonly ConcurrentDictionary<Guid, bool> _variants = new();

    public void MarkProductUsed(Guid id) {
        _products[id] = true;
    }

    public void MarkVariantUsed(Guid id) {
        _variants[id] = true;
    }

    public Task<bool> ProductInUseAsync(Guid productId, CancellationToken ct) {
        return Task.FromResult(_products.ContainsKey(productId));
    }

    public Task<bool> VariantInUseAsync(Guid variantId, CancellationToken ct) {
        return Task.FromResult(_variants.ContainsKey(variantId));
    }
}
