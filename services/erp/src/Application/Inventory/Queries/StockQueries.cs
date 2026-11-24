using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Application.Inventory.Warehouses;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Queries;

internal static class QueryAccess {
    /// <summary>Oturumun erişebildiği depolar (isteğe bağlı tek depo ile daraltılır; erişilemeyen depo 403, bilinmeyen 404).</summary>
    public static async Task<Dictionary<Guid, Warehouse>> WarehousesAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid? warehouseId,
                                                                          CancellationToken ct) {
        if (warehouseId is { } id) {
            var one = await WarehouseRules.FindAsync(db, tenant, id, ct);
            return new Dictionary<Guid, Warehouse> { [one.Id] = one };
        }

        return (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId)).ToDictionary(w => w.Id);
    }
}

// ---- stok durumu --------------------------------------------------------------------------------------------

public sealed record BalanceRowDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, string? VariantCode, Guid WarehouseId,
                                   string WarehouseName, string Unit, decimal Physical, decimal Reserved, decimal Available, decimal? MinQty,
                                   decimal? AvgCost);

/// <summary>Depo x kalem bakiyeleri. Maliyet yalnızca maliyet.read izni olana verilir.</summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListBalancesQuery(Guid? WarehouseId, string? Search, Guid? CategoryId, bool OnlyNonZero, bool BelowMinimum, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<BalanceRowDto>>;

public sealed class ListBalancesQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<ListBalancesQuery, PagedResult<BalanceRowDto>> {
    public async Task<PagedResult<BalanceRowDto>> Handle(ListBalancesQuery request, CancellationToken ct) {
        var warehouses = await QueryAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct);
        var ids        = warehouses.Keys.ToList();

        var products = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = ERP.Domain.Common.TurkishText.Fold(request.Search);
            products = products.Where(p => p.Name.ToLower().Replace("ı", "i").Contains(term) || p.Code.ToLower().Replace("ı", "i").Contains(term));
        }

        if (request.CategoryId is { } root) {
            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
            var set = new HashSet<Guid> { root };
            for (var added = true; added;) {
                added = false;
                foreach (var c in all)
                    if (c.ParentId is { } parent && set.Contains(parent) && set.Add(c.Id))
                        added = true;
            }

            products = products.Where(p => p.CategoryId != null && set.Contains(p.CategoryId.Value));
        }

        var productMap = await products.Include(p => p.Variants).ToDictionaryAsync(p => p.Id, ct);
        var productIds = productMap.Keys.ToList();

        var physical = await db.StockMovements.AsNoTracking().Where(m => ids.Contains(m.WarehouseId) && productIds.Contains(m.ProductId))
                               .GroupBy(m => new { m.WarehouseId, m.ProductId, m.VariantId })
                               .Select(g => new { g.Key.WarehouseId, g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(m => m.Quantity) }).ToListAsync(ct);
        var reserved = (await db.Reservations.AsNoTracking()
                                .Where(r => ids.Contains(r.WarehouseId) && productIds.Contains(r.ProductId) && r.Status == ReservationStatus.Active
                                            && r.ExpectedReceiptSourceId == null)
                                .GroupBy(r => new { r.WarehouseId, r.ProductId, r.VariantId })
                                .Select(g => new { g.Key.WarehouseId, g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(r => r.Quantity) }).ToListAsync(ct))
            .ToDictionary(r => (r.WarehouseId, r.ProductId, r.VariantId), r => r.Qty);
        var mins = (await db.MinStockLevels.AsNoTracking().Where(m => ids.Contains(m.WarehouseId) && productIds.Contains(m.ProductId)).ToListAsync(ct))
            .ToDictionary(m => (m.WarehouseId, m.ProductId, m.VariantId), m => m.MinQty);
        var showCost = session.HasPermission(InventoryPermissions.Cost);
        var costs = showCost
                        ? (await db.AverageCosts.AsNoTracking().Where(c => ids.Contains(c.WarehouseId) && productIds.Contains(c.ProductId)).ToListAsync(ct))
                          .ToDictionary(c => (c.WarehouseId, c.ProductId, c.VariantId), c => c.AverageCost)
                        : [];

        var rows = new List<BalanceRowDto>();
        foreach (var p in physical) {
            var key       = (p.WarehouseId, p.ProductId, p.VariantId);
            var warehouse = warehouses[p.WarehouseId];
            var product   = productMap[p.ProductId];
            var res       = reserved.GetValueOrDefault(key);
            var available = warehouse.Kind == WarehouseKind.Normal ? p.Qty - res : 0m;
            decimal? min  = mins.TryGetValue(key, out var m) ? m : null;

            if (request.OnlyNonZero && p.Qty == 0m) continue;
            if (request.BelowMinimum && (min is null || warehouse.Kind != WarehouseKind.Normal || available >= min)) continue;

            rows.Add(new BalanceRowDto(p.ProductId, product.Code, product.Name, p.VariantId,
                                       product.Variants.FirstOrDefault(v => v.Id == p.VariantId)?.Code, p.WarehouseId, warehouse.Name,
                                       product.BaseUnit, p.Qty, res, available, min, showCost && costs.TryGetValue(key, out var cost) ? cost : null));
        }

        var ordered  = rows.OrderBy(r => r.ProductCode, StringComparer.Ordinal).ThenBy(r => r.VariantCode, StringComparer.Ordinal).ThenBy(r => r.WarehouseName).ToList();
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        return new PagedResult<BalanceRowDto>(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count);
    }
}

// ---- stok kartı ---------------------------------------------------------------------------------------------

public sealed record ItemCardMovementDto(Guid Id, DateOnly Date, MovementType Type, Guid WarehouseId, string SourceType, Guid SourceId, decimal In,
                                         decimal Out, decimal Running, decimal? UnitCost, decimal? TotalCost, string? Note);

public sealed record ItemCardDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, string Unit, decimal OpeningQty,
                                 decimal ClosingQty, IReadOnlyList<ItemCardMovementDto> Movements);

/// <summary>
///     Kalem hareket dökümü: from öncesi devir (açılış miktarı), aralıktaki hareketler ve yürüyen bakiye. warehouseId verilmezse
///     erişilebilir tüm depoların toplamı. Her hareket kaynağını (sourceType/sourceId) taşır.
/// </summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ItemCardQuery(Guid ProductId, Guid? VariantId, Guid? WarehouseId, DateOnly? From, DateOnly? To) : IRequest<ItemCardDto>;

public sealed class ItemCardQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<ItemCardQuery, ItemCardDto> {
    public async Task<ItemCardDto> Handle(ItemCardQuery request, CancellationToken ct) {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
                      ?? throw new NotFoundException(request.ProductId.ToString(), "Product");
        var ids = (await QueryAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct)).Keys.ToList();

        var query = db.StockMovements.AsNoTracking().Where(m => m.ProductId == request.ProductId && m.VariantId == request.VariantId && ids.Contains(m.WarehouseId));
        var opening = 0m;
        if (request.From is { } from) {
            opening = await query.Where(m => m.Date < from).SumAsync(m => (decimal?)m.Quantity, ct) ?? 0m;
            query   = query.Where(m => m.Date >= from);
        }

        if (request.To is { } to) query = query.Where(m => m.Date <= to);

        var moves    = await query.OrderBy(m => m.Date).ThenBy(m => m.Sequence).ToListAsync(ct);
        var showCost = session.HasPermission(InventoryPermissions.Cost);
        var running  = opening;
        var rows     = new List<ItemCardMovementDto>(moves.Count);
        foreach (var m in moves) {
            running += m.Quantity;
            rows.Add(new ItemCardMovementDto(m.Id, m.Date, m.Type, m.WarehouseId, m.SourceType, m.SourceId, Math.Max(m.Quantity, 0m),
                                             Math.Max(-m.Quantity, 0m), running, showCost ? m.UnitCost : null, showCost ? m.TotalCost : null, m.Note));
        }

        return new ItemCardDto(product.Id, product.Code, product.Name, request.VariantId, product.BaseUnit, opening, running, rows);
    }
}

// ---- değerleme ----------------------------------------------------------------------------------------------

public sealed record ValuationRowDto(Guid ProductId, string ProductCode, string ProductName, Guid? VariantId, Guid WarehouseId, string WarehouseName,
                                     decimal Quantity, decimal UnitCost, decimal Value);

public sealed record ValuationDto(DateOnly Date, decimal TotalValue, IReadOnlyList<ValuationRowDto> Items);

/// <summary>Verilen tarihteki stok değeri: tarihe kadar olan hareketlerin miktar ve işaretli maliyet toplamları (yeniden değerleme dahil).</summary>
[Authorize(Permission = InventoryPermissions.Cost)]
public sealed record ValuationQuery(DateOnly? Date, Guid? WarehouseId) : IRequest<ValuationDto>;

public sealed class ValuationQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    TimeProvider          time
) : IRequestHandler<ValuationQuery, ValuationDto> {
    public async Task<ValuationDto> Handle(ValuationQuery request, CancellationToken ct) {
        var date       = request.Date ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var warehouses = await QueryAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct);
        var ids        = warehouses.Keys.ToList();

        var sums = await db.StockMovements.AsNoTracking().Where(m => ids.Contains(m.WarehouseId) && m.Date <= date)
                           .GroupBy(m => new { m.WarehouseId, m.ProductId, m.VariantId })
                           .Select(g => new { g.Key.WarehouseId, g.Key.ProductId, g.Key.VariantId, Qty = g.Sum(m => m.Quantity), Value = g.Sum(m => m.TotalCost) })
                           .ToListAsync(ct);
        sums = sums.Where(s => s.Qty != 0m || s.Value != 0m).ToList();

        var productIds = sums.Select(s => s.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var items = sums.Select(s => new ValuationRowDto(s.ProductId, products[s.ProductId].Code, products[s.ProductId].Name, s.VariantId, s.WarehouseId,
                                                         warehouses[s.WarehouseId].Name, s.Qty, s.Qty == 0m ? 0m : decimal.Round(s.Value / s.Qty, 6),
                                                         decimal.Round(s.Value, 2)))
                        .OrderBy(r => r.ProductCode, StringComparer.Ordinal).ThenBy(r => r.WarehouseName).ToList();
        return new ValuationDto(date, items.Sum(i => i.Value), items);
    }
}
