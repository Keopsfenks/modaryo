using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing.Documents;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

[Authorize(Permission = SalesPermissions.Read)]
public sealed record ListOrdersQuery(
    SalesOrderStatus? Status,
    Guid?             CurrentAccountId,
    Guid?             SalesRepUserId,
    string?           Search,
    int               Page     = 1,
    int               PageSize = 25
) : IRequest<OrderListResult>;

public sealed class ListOrdersQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListOrdersQuery, OrderListResult> {
    public async Task<OrderListResult> Handle(ListOrdersQuery request, CancellationToken ct) {
        var query = db.SalesOrders.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(o => branches.Contains(o.BranchId));
        }

        if (request.Status is { } status) query = query.Where(o => o.Status == status);
        if (request.CurrentAccountId is { } cari) query = query.Where(o => o.CurrentAccountId == cari);
        if (request.SalesRepUserId is { } rep) query = query.Where(o => o.SalesRepUserId == rep);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(o => o.Number.ToLower().Contains(term) || db.CurrentAccounts.Any(c => c.Id == o.CurrentAccountId && c.Name.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(o => o.Created).ThenByDescending(o => o.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(o => new {
                                  o.Id, o.Number, o.Status, o.InvoiceState, o.CurrentAccountId,
                                  CustomerName = db.CurrentAccounts.Where(c => c.Id == o.CurrentAccountId).Select(c => c.Name).FirstOrDefault(),
                                  o.Date, o.Currency, o.Total, o.SalesRepName
                              }).ToListAsync(ct);
        return new OrderListResult(rows.Select(r => new OrderRowDto(r.Id, r.Number, r.Status, r.InvoiceState, r.CurrentAccountId, r.CustomerName ?? string.Empty, r.Date, r.Currency,
                                                                    r.Total, r.SalesRepName)).ToList(), total);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetOrderQuery(Guid Id) : IRequest<OrderDto>;

public sealed class GetOrderQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    OrderMapper           mapper
) : IRequestHandler<GetOrderQuery, OrderDto> {
    public async Task<OrderDto> Handle(GetOrderQuery request, CancellationToken ct) {
        var order = await OrderRules.LoadAsync(db, tenant, request.Id, false, ct);
        return await mapper.ToDtoAsync(order, null, ct);
    }
}

/// <summary>Bekleyen sevk ve fatura: onaylı siparişlerin açık miktarı olan satırları.</summary>
[Authorize(Permission = SalesPermissions.Read)]
public sealed record PendingOrdersQuery(Guid? CurrentAccountId, string? Search, bool? ToShip, bool? ToInvoice, int Page = 1, int PageSize = 50) : IRequest<PendingListResult>;

public sealed class PendingOrdersQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<PendingOrdersQuery, PendingListResult> {
    public async Task<PendingListResult> Handle(PendingOrdersQuery request, CancellationToken ct) {
        var statuses = new[] { SalesOrderStatus.Approved, SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Shipped };
        var orders   = db.SalesOrders.AsNoTracking().Where(o => statuses.Contains(o.Status));
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            orders = orders.Where(o => branches.Contains(o.BranchId));
        }

        if (request.CurrentAccountId is { } cari) orders = orders.Where(o => o.CurrentAccountId == cari);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            orders = orders.Where(o => o.Number.ToLower().Contains(term) || db.CurrentAccounts.Any(c => c.Id == o.CurrentAccountId && c.Name.ToLower().Contains(term)));
        }

        var rows = await orders.SelectMany(o => o.Lines, (o, l) => new {
                                   o.Id, o.Number, o.CurrentAccountId, o.Created,
                                   CustomerName = db.CurrentAccounts.Where(c => c.Id == o.CurrentAccountId).Select(c => c.Name).FirstOrDefault(),
                                   Line = l
                               }).ToListAsync(ct);

        // Teslim edilemeyen (DeliveryFailed) sevkin faturalanmamış miktarı faturalanamaz (yalnızca Shipped/Delivered faturalanır); faturalanacak miktardan düşülür.
        var lineIds = rows.Select(r => r.Line.Id).ToList();
        var failedOpen = (await db.ShipmentLines.AsNoTracking()
                                  .Where(l => lineIds.Contains(l.SalesOrderLineId) && db.Shipments.Any(s => s.Id == l.ShipmentId && s.Status == ShipmentStatus.DeliveryFailed))
                                  .Select(l => new { l.SalesOrderLineId, Open = l.Quantity - l.InvoicedQuantity }).ToListAsync(ct))
                         .GroupBy(l => l.SalesOrderLineId).ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0m, l.Open)));

        var all = rows.Select(r => new PendingLineDto(r.Id, r.Number, r.CurrentAccountId, r.CustomerName ?? string.Empty, r.Line.Id, r.Line.Sort, r.Line.ProductId, r.Line.Description,
                                                      r.Line.Unit, r.Line.Quantity, r.Line.ShippedQuantity, r.Line.InvoicedQuantity,
                                                      Math.Max(0m, r.Line.Quantity - r.Line.ShippedQuantity),
                                                      Math.Max(0m, r.Line.ShippedQuantity - r.Line.InvoicedQuantity - failedOpen.GetValueOrDefault(r.Line.Id))))
                      .Where(l => (l.OpenToShip > 0m && request.ToInvoice != true) || (l.OpenToInvoice > 0m && request.ToShip != true))
                      .Where(l => request.ToShip != true || l.OpenToShip > 0m)
                      .Where(l => request.ToInvoice != true || l.OpenToInvoice > 0m)
                      .OrderBy(l => l.OrderNumber).ThenBy(l => l.Sort).ToList();
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        return new PendingListResult(all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetOrderProformaQuery(Guid Id) : IRequest<OrderPdfFile>;

public sealed class GetOrderProformaQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IDocumentPdf          pdf
) : IRequestHandler<GetOrderProformaQuery, OrderPdfFile> {
    public async Task<OrderPdfFile> Handle(GetOrderProformaQuery request, CancellationToken ct) {
        var order   = await OrderRules.LoadAsync(db, tenant, request.Id, false, ct);
        var content = await pdf.RenderSalesOrderAsync(order.Id, true, ct);
        return new OrderPdfFile(content, InvoiceMail.FileName(order.Number, order.Id));
    }
}
