using ERP.Application.Common.Security;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Receipts;

[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record ListReceiptsQuery(
    Guid?               PurchaseOrderId,
    Guid?               SupplierId,
    GoodsReceiptStatus? Status,
    string?             Search,
    int                 Page     = 1,
    int                 PageSize = 25
) : IRequest<ReceiptListResult>;

public sealed class ListReceiptsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session
) : IRequestHandler<ListReceiptsQuery, ReceiptListResult> {
    public async Task<ReceiptListResult> Handle(ListReceiptsQuery request, CancellationToken ct) {
        var showPrices = session.HasPermission(PurchasingPermissions.PurchasePrice);

        var query = db.GoodsReceipts.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(r => branches.Contains(r.BranchId));
        }

        if (request.PurchaseOrderId is { } poId) query = query.Where(r => r.PurchaseOrderId == poId);
        if (request.SupplierId is { } supplierId) query = query.Where(r => r.SupplierId == supplierId);
        if (request.Status is { } status) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Number.ToLower().Contains(term) || (r.SupplierDeliveryNo != null && r.SupplierDeliveryNo.ToLower().Contains(term))
                                     || db.CurrentAccounts.Any(c => c.Id == r.SupplierId && c.Name.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(r => r.Date).ThenByDescending(r => r.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(r => new {
                                  r.Id, r.Number, r.Status, r.PurchaseOrderId,
                                  PoNumber     = db.PurchaseOrders.Where(p => p.Id == r.PurchaseOrderId).Select(p => p.Number).FirstOrDefault(),
                                  r.SupplierId,
                                  SupplierName = db.CurrentAccounts.Where(c => c.Id == r.SupplierId).Select(c => c.Name).FirstOrDefault(),
                                  r.BranchId, r.WarehouseId, r.Date, r.Currency, LineCount = r.Lines.Count,
                                  Total = r.Lines.Sum(l => Math.Round((l.UnitPrice ?? 0m) * l.Quantity, 2))
                              }).ToListAsync(ct);

        return new ReceiptListResult(rows.Select(r => new ReceiptRowDto(r.Id, r.Number, r.Status, r.PurchaseOrderId, r.PoNumber, r.SupplierId, r.SupplierName ?? string.Empty,
                                                                          r.BranchId, r.WarehouseId, r.Date, r.Currency, showPrices ? r.Total : null, r.LineCount)).ToList(), total);
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record GetReceiptQuery(Guid Id) : IRequest<ReceiptDto>;

public sealed class GetReceiptQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ReceiptMapper         mapper
) : IRequestHandler<GetReceiptQuery, ReceiptDto> {
    public async Task<ReceiptDto> Handle(GetReceiptQuery request, CancellationToken ct) {
        var receipt = await ReceiptRules.LoadAsync(db, tenant, request.Id, false, ct);
        return await mapper.ToDtoAsync(receipt, [], ct);
    }
}
