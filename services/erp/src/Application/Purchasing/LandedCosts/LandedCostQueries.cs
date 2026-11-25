using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.LandedCosts;

public sealed record LandedSourceDto(Guid Id, Guid? InvoiceId, string Description, string? CostType, decimal AmountTl);

public sealed record LandedAllocationDto(
    Guid                       ReceiptId,
    string                     ReceiptNumber,
    Guid                       ReceiptLineId,
    Guid                       ProductId,
    string                     ProductCode,
    string                     ProductName,
    decimal                    BaseQuantity,
    decimal?                   WeightKgPerUnit,
    decimal                    TlValue,
    decimal                    AllocatedTl,
    IReadOnlyDictionary<string, decimal> ByCostType
);

public sealed record LandedCostDto(
    Guid                             Id,
    string                           Number,
    LandedCostStatus                 Status,
    DateOnly                         Date,
    AllocationKey                    Key,
    Guid?                            ImportFileId,
    string?                          Notes,
    string?                          CancelReason,
    decimal                          TotalTl,
    IReadOnlyList<LandedSourceDto>   Sources,
    IReadOnlyList<LandedAllocationDto> Allocations,
    IReadOnlyDictionary<string, decimal> ByCostType
);

public sealed record LandedCostListItemDto(Guid Id, string Number, LandedCostStatus Status, DateOnly Date, AllocationKey Key, decimal TotalTl, Guid? ImportFileId);

public sealed class LandedCostMapper(IApplicationDbContext db) {
    public async Task<LandedCostDto> ToDtoAsync(LandedCost landed, CancellationToken ct) {
        var receiptIds = landed.Targets.Select(t => t.GoodsReceiptId).Distinct().ToList();
        var receipts   = await db.GoodsReceipts.AsNoTracking().Where(r => receiptIds.Contains(r.Id)).Select(r => new { r.Id, r.Number }).ToDictionaryAsync(r => r.Id, ct);
        var lineIds    = landed.Targets.Select(t => t.ReceiptLineId).ToList();
        var lines      = await db.GoodsReceiptLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.ProductId }).ToDictionaryAsync(l => l.Id, ct);
        var productIds = lines.Values.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);

        // Masraf türüne göre kırılım: dağıtım deterministiktir; kayıtlı hedef anlık değerleriyle yeniden hesaplanır.
        var snapshot = landed.Targets.Select(t => new LandedTarget(t.ReceiptLineId, t.TlValue, t.BaseQuantity, t.WeightKgPerUnit)).ToList();
        var shares   = snapshot.Count == 0 ? [] : LandedCostRules.AllocateBySource(landed.Sources, landed.Key, snapshot);
        var allocations = landed.Targets.OrderBy(t => receipts[t.GoodsReceiptId].Number).ThenBy(t => t.Id).Select(t => {
            var product = products[lines[t.ReceiptLineId].ProductId];
            return new LandedAllocationDto(t.GoodsReceiptId, receipts[t.GoodsReceiptId].Number, t.ReceiptLineId, product.Id, product.Code, product.Name, t.BaseQuantity,
                                           t.WeightKgPerUnit, t.TlValue, t.AllocatedTl, shares.GetValueOrDefault(t.ReceiptLineId) ?? new Dictionary<string, decimal>());
        }).ToList();
        var byType = landed.Sources.GroupBy(s => s.CostType ?? "diger").ToDictionary(g => g.Key, g => g.Sum(s => s.AmountTl));
        return new LandedCostDto(landed.Id, landed.Number, landed.Status, landed.Date, landed.Key, landed.ImportFileId, landed.Notes, landed.CancelReason, landed.TotalTl,
                                 landed.Sources.Select(s => new LandedSourceDto(s.Id, s.InvoiceId, s.Description, s.CostType, s.AmountTl)).ToList(), allocations, byType);
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record GetLandedCostQuery(Guid Id) : IRequest<LandedCostDto>;

public sealed class GetLandedCostQueryHandler(IApplicationDbContext db, LandedCostMapper mapper) : IRequestHandler<GetLandedCostQuery, LandedCostDto> {
    public async Task<LandedCostDto> Handle(GetLandedCostQuery request, CancellationToken ct) {
        var landed = await db.LandedCosts.AsNoTracking().Include(c => c.Sources).Include(c => c.Targets).FirstOrDefaultAsync(c => c.Id == request.Id, ct)
                     ?? throw new NotFoundException(request.Id.ToString(), "LandedCost");
        return await mapper.ToDtoAsync(landed, ct);
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
[Authorize(Permission = PurchasingPermissions.PurchasePrice)]
public sealed record ListLandedCostsQuery(LandedCostStatus? Status, Guid? ImportFileId) : IRequest<IReadOnlyList<LandedCostListItemDto>>;

public sealed class ListLandedCostsQueryHandler(IApplicationDbContext db) : IRequestHandler<ListLandedCostsQuery, IReadOnlyList<LandedCostListItemDto>> {
    public async Task<IReadOnlyList<LandedCostListItemDto>> Handle(ListLandedCostsQuery request, CancellationToken ct) {
        var query = db.LandedCosts.AsNoTracking().AsQueryable();
        if (request.Status is { } status) query = query.Where(c => c.Status == status);
        if (request.ImportFileId is { } file) query = query.Where(c => c.ImportFileId == file);
        return await query.OrderByDescending(c => c.Date).ThenByDescending(c => c.Number)
                          .Select(c => new LandedCostListItemDto(c.Id, c.Number, c.Status, c.Date, c.Key, c.TotalTl, c.ImportFileId)).ToListAsync(ct);
    }
}
