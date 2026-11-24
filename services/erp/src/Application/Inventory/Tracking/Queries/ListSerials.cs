using ERP.Application.Common.Security;
using ERP.Application.Common.Interfaces;

namespace ERP.Application.Inventory.Tracking.Queries;

public sealed record SerialDto(Guid Id, string Serial, Guid? WarehouseId, bool InStock);

/// <summary>Kalemin seri numaraları. inStock true: stokta olanlar, false: stokta olmayanlar, verilmezse hepsi.</summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record ListSerialsQuery(Guid ProductId, Guid? VariantId, Guid? WarehouseId, bool? InStock) : IRequest<IReadOnlyList<SerialDto>>;

internal sealed class ListSerialsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListSerialsQuery, IReadOnlyList<SerialDto>> {
    public async Task<IReadOnlyList<SerialDto>> Handle(ListSerialsQuery request, CancellationToken ct) {
        var allowed = await TrackingAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct);

        var query = db.SerialNumbers.AsNoTracking().Where(s => s.ProductId == request.ProductId && s.VariantId == request.VariantId);
        if (request.WarehouseId is { } warehouse) query = query.Where(s => s.WarehouseId == warehouse);
        if (request.InStock is true) query = query.Where(s => s.WarehouseId != null);
        if (request.InStock is false) query = query.Where(s => s.WarehouseId == null);

        var rows = await query.OrderBy(s => s.Serial).ToListAsync(ct);
        // Erişilemeyen şubenin deposundaki seriler görünmez.
        return rows.Where(s => s.WarehouseId is null || allowed.Contains(s.WarehouseId.Value))
                   .Select(s => new SerialDto(s.Id, s.Serial, s.WarehouseId, s.WarehouseId is not null)).ToList();
    }
}
