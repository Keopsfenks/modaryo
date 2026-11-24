using ERP.Application.Common.Interfaces;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Ledger;

/// <summary>Depo x kalem bakiyeleri. Maliyet yalnızca maliyet.read izni olana verilir; şube kapsamı dışındaki depolar görünmez.</summary>
public sealed class BalanceReader(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) {
    public async Task<IReadOnlyList<StockBalance>> ForItemsAsync(IReadOnlyCollection<(Guid ProductId, Guid? VariantId)> items,
                                                                 Guid?                                               warehouseId,
                                                                 CancellationToken                                   ct
    ) {
        if (items.Count == 0) return [];

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var wanted     = items.ToHashSet();

        var movements = db.StockMovements.AsNoTracking().Where(m => productIds.Contains(m.ProductId));
        var reserved  = db.Reservations.AsNoTracking()
                          .Where(r => productIds.Contains(r.ProductId) && r.Status == ReservationStatus.Active
                                      && r.ExpectedReceiptSourceId == null);
        var costs = db.AverageCosts.AsNoTracking().Where(c => productIds.Contains(c.ProductId));
        if (warehouseId is { } only) {
            movements = movements.Where(m => m.WarehouseId == only);
            reserved  = reserved.Where(r => r.WarehouseId == only);
            costs     = costs.Where(c => c.WarehouseId == only);
        }

        var physical = (await movements.GroupBy(m => new { m.WarehouseId, m.ProductId, m.VariantId })
                                       .Select(g => new { g.Key.WarehouseId, g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(m => m.Quantity) })
                                       .ToListAsync(ct))
            .Where(r => wanted.Contains((r.ProductId, r.VariantId))).ToList();
        var reservedRows = (await reserved.GroupBy(r => new { r.WarehouseId, r.ProductId, r.VariantId })
                                          .Select(g => new { g.Key.WarehouseId, g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(r => r.Quantity) })
                                          .ToListAsync(ct))
            .Where(r => wanted.Contains((r.ProductId, r.VariantId))).ToList();
        var costRows = await costs.ToListAsync(ct);

        var keys = physical.Select(p => (p.WarehouseId, p.ProductId, p.VariantId))
                           .Concat(reservedRows.Select(r => (r.WarehouseId, r.ProductId, r.VariantId)))
                           .Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, ct);
        var showCost   = session.HasPermission(InventoryPermissions.Cost);

        var result = new List<StockBalance>();
        foreach (var (wh, product, variant) in keys) {
            if (!warehouses.TryGetValue(wh, out var warehouse) || !tenant.CanAccessBranch(warehouse.BranchId)) continue;

            var phys = physical.FirstOrDefault(p => p.WarehouseId == wh && p.ProductId == product && p.VariantId == variant)?.Qty ?? 0m;
            var res  = reservedRows.FirstOrDefault(r => r.WarehouseId == wh && r.ProductId == product && r.VariantId == variant)?.Qty ?? 0m;
            var cost = costRows.FirstOrDefault(c => c.WarehouseId == wh && c.ProductId == product && c.VariantId == variant)?.AverageCost;
            result.Add(new StockBalance(wh, product, variant, phys, res,
                                        warehouse.Kind == WarehouseKind.Normal ? phys - res : 0m,
                                        showCost ? cost : null));
        }

        return result;
    }
}
