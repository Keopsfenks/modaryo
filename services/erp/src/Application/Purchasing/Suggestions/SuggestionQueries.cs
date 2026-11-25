using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Ledger;
using ERP.Application.Purchasing.Invoices;
using ERP.Domain.Inventory;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Suggestions;

public sealed record SuggestionLineDto(
    Guid     ProductId,
    string   ProductCode,
    string   ProductName,
    Guid?    VariantId,
    Guid     WarehouseId,
    decimal  Available,
    decimal  OnOrder,
    decimal  MinQty,
    decimal  SuggestedQty,
    decimal? LastPrice
);

public sealed record SuggestionGroupDto(Guid? SupplierId, string SupplierName, IReadOnlyList<SuggestionLineDto> Lines);

public sealed record SuggestionResult(IReadOnlyList<SuggestionGroupDto> Groups);

/// <summary>
///     Minimum stoğun altındaki kalemler varsayılan tedarikçiye göre gruplanır. Açık siparişlerdeki (onaylı, henüz gelmemiş) miktar
///     "yolda" sayılır ve ihtiyacı karşılar. Önerilen miktar: max(sipariş miktarı, minimum - (kullanılabilir + yolda)).
///     Son alış fiyatı yalnızca fiyat.alis yetkisiyle döner.
/// </summary>
[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record GetSuggestionsQuery(Guid? BranchId) : IRequest<SuggestionResult>;

public sealed class GetSuggestionsQueryHandler(
    IApplicationDbContext    db,
    ICurrentTenant           tenant,
    ICurrentSession          session,
    BalanceReader            balances,
    PurchasePriceStatsReader stats
) : IRequestHandler<GetSuggestionsQuery, SuggestionResult> {
    public const string NoSupplierName = "Tedarikçisiz";

    public async Task<SuggestionResult> Handle(GetSuggestionsQuery request, CancellationToken ct) {
        if (request.BranchId is { } requested) BranchGuard.Ensure(tenant, requested);

        var warehouses = (await db.Warehouses.AsNoTracking().Where(w => w.Kind == WarehouseKind.Normal && w.IsActive).ToListAsync(ct))
                         .Where(w => tenant.CanAccessBranch(w.BranchId) && (request.BranchId == null || w.BranchId == request.BranchId))
                         .OrderBy(w => w.Code).ToList();
        var warehouseIds = warehouses.Select(w => w.Id).ToList();
        var levels = await db.MinStockLevels.AsNoTracking().Where(m => warehouseIds.Contains(m.WarehouseId) && m.MinQty > 0m).ToListAsync(ct);
        if (levels.Count == 0) return new SuggestionResult([]);

        var items      = levels.Select(l => (l.ProductId, l.VariantId)).Distinct().ToList();
        var current    = await balances.ForItemsAsync(items, null, ct);
        var productIds = levels.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Suppliers).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Yolda: açık siparişlerin kalan miktarı (taban birim), şube + kalem başına.
        var open = new Dictionary<(Guid Branch, Guid Product, Guid? Variant), decimal>();
        var openLines = await db.PurchaseOrderLines.AsNoTracking().Where(l => productIds.Contains(l.ProductId))
                                .Join(db.PurchaseOrders.Where(p => p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived),
                                      l => l.PurchaseOrderId, p => p.Id,
                                      (l, p) => new { p.BranchId, l.ProductId, l.VariantId, l.Quantity, l.ReceivedQuantity, l.ClosedQuantity, l.Factor })
                                .ToListAsync(ct);
        foreach (var l in openLines) {
            var quantity = Math.Max(0m, l.Quantity - l.ReceivedQuantity - l.ClosedQuantity) * l.Factor;
            if (quantity <= 0m) continue;
            var key = (l.BranchId, l.ProductId, l.VariantId);
            open[key] = open.GetValueOrDefault(key) + quantity;
        }

        var lastPrices = new Dictionary<(Guid, Guid?), decimal>();
        if (session.HasPermission(PurchasingPermissions.PurchasePrice))
            foreach (var group in (await stats.GetAsync(null, null, ct)).GroupBy(s => (s.ProductId, s.VariantId)))
                lastPrices[group.Key] = group.OrderByDescending(s => s.LastPurchaseDate).First().LastUnitPriceTl;

        var order    = warehouses.Select((w, i) => (w.Id, Index: i)).ToDictionary(x => x.Id, x => x.Index);
        var branchOf = warehouses.ToDictionary(w => w.Id, w => w.BranchId);
        var lines    = new List<(Guid? Supplier, SuggestionLineDto Line)>();
        foreach (var level in levels.OrderBy(l => order[l.WarehouseId]).ThenBy(l => products[l.ProductId].Code)) {
            var available = current.FirstOrDefault(b => b.WarehouseId == level.WarehouseId && b.ProductId == level.ProductId && b.VariantId == level.VariantId)?.Available ?? 0m;
            if (available >= level.MinQty) continue;

            // Açık sipariş miktarı eksiği olan depolara sırayla dağıtılır.
            var key     = (branchOf[level.WarehouseId], level.ProductId, level.VariantId);
            var onOrder = Math.Min(open.GetValueOrDefault(key), level.MinQty - available);
            if (onOrder > 0m) open[key] -= onOrder;
            var effective = available + onOrder;
            if (effective >= level.MinQty) continue;

            var product   = products[level.ProductId];
            var suggested = Math.Max(level.ReorderQty ?? 0m, level.MinQty - effective);
            var supplier  = product.Suppliers.FirstOrDefault(s => s.IsDefault)?.CurrentAccountId;
            lines.Add((supplier, new SuggestionLineDto(product.Id, product.Code, product.Name, level.VariantId, level.WarehouseId, available, onOrder,
                                                       level.MinQty, suggested, lastPrices.TryGetValue((product.Id, level.VariantId), out var p) ? p : null)));
        }

        var supplierIds = lines.Where(l => l.Supplier is not null).Select(l => l.Supplier!.Value).Distinct().ToList();
        var names       = await db.CurrentAccounts.AsNoTracking().Where(c => supplierIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var groups = lines.GroupBy(l => l.Supplier)
                          .Select(g => new SuggestionGroupDto(g.Key, g.Key is { } id ? names.GetValueOrDefault(id, "?") : NoSupplierName,
                                                              g.Select(x => x.Line).OrderBy(x => x.ProductCode).ToList()))
                          .OrderBy(g => g.SupplierId is null ? 1 : 0).ThenBy(g => g.SupplierName)
                          .ToList();
        return new SuggestionResult(groups);
    }
}
