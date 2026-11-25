using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record ListPosQuery(
    PurchaseOrderStatus? Status,
    Guid?                SupplierId,
    bool?                Overdue,
    string?              Search,
    int                  Page     = 1,
    int                  PageSize = 25
) : IRequest<PoListResult>;

public sealed class ListPosQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    TimeProvider          time
) : IRequestHandler<ListPosQuery, PoListResult> {
    public async Task<PoListResult> Handle(ListPosQuery request, CancellationToken ct) {
        var today      = CurrentAccountClock.Today(time);
        var showPrices = session.HasPermission(PurchasingPermissions.PurchasePrice);

        var query = db.PurchaseOrders.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(p => branches.Contains(p.BranchId));
        }
        if (request.Status is { } status) query = query.Where(p => p.Status == status);
        if (request.SupplierId is { } supplierId) query = query.Where(p => p.SupplierId == supplierId);
        if (request.Overdue == true)
            query = query.Where(p => (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived)
                                     && p.Lines.Any(l => l.Quantity - l.ReceivedQuantity - l.ClosedQuantity > 0m
                                                         && (l.ExpectedDate ?? p.ExpectedDate ?? p.Date) < today));
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Number.ToLower().Contains(term)
                                     || db.CurrentAccounts.Any(c => c.Id == p.SupplierId && c.Name.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(p => new {
                                  p.Id, p.Number, p.Status, p.SupplierId,
                                  SupplierName = db.CurrentAccounts.Where(c => c.Id == p.SupplierId).Select(c => c.Name).FirstOrDefault(),
                                  p.BranchId, p.Date, p.ExpectedDate, p.Currency, p.Total, LineCount = p.Lines.Count,
                                  Overdue = (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived)
                                            && p.Lines.Any(l => l.Quantity - l.ReceivedQuantity - l.ClosedQuantity > 0m
                                                                && (l.ExpectedDate ?? p.ExpectedDate ?? p.Date) < today)
                              }).ToListAsync(ct);

        return new PoListResult(rows.Select(r => new PoRowDto(r.Id, r.Number, r.Status, r.SupplierId, r.SupplierName ?? string.Empty, r.BranchId, r.Date,
                                                               r.ExpectedDate, r.Currency, showPrices ? r.Total : null, r.LineCount, r.Overdue)).ToList(), total);
    }
}

[Authorize(Permission = PurchasingPermissions.Read)]
public sealed record GetPoQuery(Guid Id) : IRequest<PoDto>;

public sealed class GetPoQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    PoMapper              mapper
) : IRequestHandler<GetPoQuery, PoDto> {
    public async Task<PoDto> Handle(GetPoQuery request, CancellationToken ct) {
        var po = await PoRules.LoadAsync(db, tenant, request.Id, false, ct);
        return await mapper.ToDtoAsync(po, ct);
    }
}
