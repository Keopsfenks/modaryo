using ERP.Application.Common.Security;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Tracking.Queries;

public sealed record LotPickDto(Guid LotId, string LotNo, DateOnly? Expiry, decimal Quantity);

/// <summary>FEFO önerisi: son kullanma tarihi en yakın lottan başlar (tarihsiz en sonda). Stok yetmezse eldeki kadarını önerir.</summary>
[Authorize(Permission = InventoryPermissions.Read)]
public sealed record SuggestLotsQuery(Guid ProductId, Guid? VariantId, Guid WarehouseId, decimal Qty) : IRequest<IReadOnlyList<LotPickDto>>;

public sealed class SuggestLotsQueryValidator : AbstractValidator<SuggestLotsQuery> {
    public SuggestLotsQueryValidator() {
        RuleFor(q => q.Qty).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.");
    }
}

internal sealed class SuggestLotsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    TrackingValidator     tracking
) : IRequestHandler<SuggestLotsQuery, IReadOnlyList<LotPickDto>> {
    public async Task<IReadOnlyList<LotPickDto>> Handle(SuggestLotsQuery request, CancellationToken ct) {
        await TrackingAccess.WarehousesAsync(db, tenant, request.WarehouseId, ct);
        var balances = await tracking.LotBalancesAsync(request.ProductId, request.VariantId, request.WarehouseId, ct);
        var ids      = balances.Where(b => b.Value > 0m).Select(b => b.Key.LotId).ToList();
        var lots     = await db.Lots.AsNoTracking().Where(l => ids.Contains(l.Id)).ToListAsync(ct);

        var picks = FefoPicker.Suggest(lots.Select(l => (l.Id, l.ExpiryDate, l.CreatedAt, balances[(request.WarehouseId, l.Id)])).ToList(),
                                       request.Qty);
        return picks.Select(p => {
                        var lot = lots.First(l => l.Id == p.LotId);
                        return new LotPickDto(lot.Id, lot.LotNo, lot.ExpiryDate, p.Qty);
                    }).ToList();
    }
}
