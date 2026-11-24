using ERP.Application.Common.Interfaces;

namespace ERP.Application.Inventory.Usage;

/// <summary>
///     Katalog kartının stok tarafında kullanımda olup olmadığını söyler: stok hareketi, rezervasyon ya da bir set
///     ürünün bileşeni olmak kartı (ya da varyantı) kullanımda yapar. Katalogdaki product_in_use / variant_in_use
///     korumalarının gerçek kaynağıdır.
/// </summary>
public sealed class InventoryItemUsageProvider(IApplicationDbContext db) : IItemUsageProvider {
    public async Task<bool> ProductInUseAsync(Guid productId, CancellationToken ct) {
        if (await db.StockMovements.AnyAsync(m => m.ProductId == productId, ct)) return true;
        if (await db.Reservations.AnyAsync(r => r.ProductId == productId, ct)) return true;

        return (await BundleComponentsAsync(ct)).Any(c => c.ProductId == productId);
    }

    public async Task<bool> VariantInUseAsync(Guid variantId, CancellationToken ct) {
        if (await db.StockMovements.AnyAsync(m => m.VariantId == variantId, ct)) return true;
        if (await db.Reservations.AnyAsync(r => r.VariantId == variantId, ct)) return true;

        return (await BundleComponentsAsync(ct)).Any(c => c.VariantId == variantId);
    }

    private async Task<List<ERP.Domain.Inventory.BundleComponent>> BundleComponentsAsync(CancellationToken ct) {
        var lists = await db.Products.AsNoTracking().Where(p => p.IsBundle).Select(p => p.BundleComponents).ToListAsync(ct);
        return lists.SelectMany(l => l).ToList();
    }
}

/// <summary>
///     Şubenin stok tarafında kullanımda olup olmadığını söyler: kullanıcının açtığı bir depo ya da hareketi/sayımı
///     olan varsayılan depo şubeyi kullanımda yapar. Yalnızca otomatik açılmış, boş varsayılan depo engel değildir.
/// </summary>
public sealed class InventoryBranchUsageProvider(IApplicationDbContext db) : IBranchUsageProvider {
    public async Task<bool> IsInUseAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        var warehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                             .Where(w => w.TenantId == tenantId && w.BranchId == branchId)
                             .Select(w => new { w.Id, w.IsDefault }).ToListAsync(ct);
        if (warehouses.Count == 0) return false;
        if (warehouses.Any(w => !w.IsDefault)) return true;

        var ids = warehouses.Select(w => w.Id).ToList();
        return await db.StockMovements.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenantId && ids.Contains(m.WarehouseId), ct)
               || await db.StockCounts.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId && ids.Contains(c.WarehouseId), ct)
               || await db.Reservations.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && ids.Contains(r.WarehouseId), ct);
    }
}
