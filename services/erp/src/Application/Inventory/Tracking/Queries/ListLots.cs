using ERP.Application.Common.Security;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Warehouses;

namespace ERP.Application.Inventory.Tracking.Queries;

internal static class TrackingAccess {
    /// <summary>Seri/lot modülü kapalıysa 403; verilen depo erişilebilir değilse 403 / 404. Erişilebilir depo kimliklerini döndürür.</summary>
    public static async Task<HashSet<Guid>> WarehousesAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid? warehouseId, CancellationToken ct) {
        if (!tenant.HasModule(TrackingValidator.Module)) throw InventoryErrors.ModuleDisabled();

        if (warehouseId is { } id) {
            await WarehouseRules.FindAsync(db, tenant, id, ct);
            return [id];
        }

        return (await db.Warehouses.AsNoTracking().ToListAsync(ct)).Where(w => tenant.CanAccessBranch(w.BranchId)).Select(w => w.Id).ToHashSet();
    }
}

public sealed record LotDto(Guid LotId, string LotNo, DateOnly? Expiry, decimal Available);

/// <summary>Kalemin lotları ve stoktaki miktarları. warehouseId verilmezse erişilebilir tüm depoların toplamıdır.</summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListLotsQuery(Guid ProductId, Guid? VariantId, Guid? WarehouseId) : IRequest<IReadOnlyList<LotDto>>;

internal sealed class ListLotsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    TrackingValidator     tracking
) : IRequestHandler<ListLotsQuery, IReadOnlyList<LotDto>> {
    public async Task<IReadOnlyList<LotDto>> Handle(ListLotsQuery request, CancellationToken ct) {
        var allowed  = await TrackingAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct);
        var balances = await tracking.LotBalancesAsync(request.ProductId, request.VariantId, request.WarehouseId, ct);
        var totals = balances.Where(b => allowed.Contains(b.Key.WarehouseId))
                             .GroupBy(b => b.Key.LotId).ToDictionary(g => g.Key, g => g.Sum(b => b.Value));

        var lots = await db.Lots.AsNoTracking().Where(l => l.ProductId == request.ProductId && l.VariantId == request.VariantId).ToListAsync(ct);
        return lots.Where(l => totals.GetValueOrDefault(l.Id) > 0m)
                   .OrderBy(l => l.ExpiryDate is null).ThenBy(l => l.ExpiryDate).ThenBy(l => l.CreatedAt)
                   .Select(l => new LotDto(l.Id, l.LotNo, l.ExpiryDate, totals[l.Id])).ToList();
    }
}
