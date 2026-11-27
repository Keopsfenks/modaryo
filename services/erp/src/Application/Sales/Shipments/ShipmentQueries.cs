using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Shipments;

public sealed class ShipmentMapper(IApplicationDbContext db) {
    public async Task<ShipmentDto> ToDtoAsync(Shipment s, CancellationToken ct) {
        var order    = await db.SalesOrders.AsNoTracking().Where(o => o.Id == s.SalesOrderId).Select(o => o.Number).FirstOrDefaultAsync(ct);
        var customer = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == s.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var lines = s.Lines.OrderBy(l => l.Sort)
                     .Select(l => new ShipmentLineDto(l.Id, l.Sort, l.SalesOrderLineId, l.ProductId, l.VariantId, l.Description, l.Unit, l.Factor, l.Quantity, l.Width, l.Length,
                                                      l.InvoicedQuantity, l.Lots, l.Serials)).ToList();
        var parcels = (await db.Parcels.AsNoTracking().Where(p => p.ShipmentId == s.Id).OrderBy(p => p.Index).ToListAsync(ct))
                      .Select(p => new ParcelDto(p.Id, p.Index, p.WeightKg, p.WidthCm, p.LengthCm, p.HeightCm, p.Desi, p.Contents.Select(c => new ParcelContentDto(c.ShipmentLineId, c.Quantity)).ToList())).ToList();
        var attempts = (await db.DeliveryAttempts.AsNoTracking().Where(a => a.ShipmentId == s.Id).OrderBy(a => a.AttemptNo).ToListAsync(ct))
                       .Select(a => new AttemptDto(a.AttemptNo, a.SentAt, a.Outcome.ToString(), a.ResolvedAt, a.FailureReason)).ToList();
        return new ShipmentDto(s.Id, s.Number, s.Status, s.SalesOrderId, order ?? string.Empty, s.CurrentAccountId, customer ?? string.Empty, s.BranchId, s.WarehouseId, s.Date,
                               s.ShippedAt, s.DeliveredAt, s.ReceivedBy, s.DeliveryAddress, s.CargoNote, s.Notes, s.CancelReason, s.InvoiceId, s.Version, lines,
                               s.CarrierCode, s.TrackingNo, s.IrsaliyeNo, s.DispatchAt, s.Vehicle, s.Driver, s.PickListId, parcels.Sum(p => p.WeightKg), parcels.Sum(p => p.Desi), parcels, attempts);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record ListShipmentsQuery(
    ShipmentStatus? Status,
    Guid?           OrderId,
    Guid?           CurrentAccountId,
    string?         Search,
    int             Page     = 1,
    int             PageSize = 25
) : IRequest<ShipmentListResult>;

public sealed class ListShipmentsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListShipmentsQuery, ShipmentListResult> {
    public async Task<ShipmentListResult> Handle(ListShipmentsQuery request, CancellationToken ct) {
        var query = db.Shipments.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(s => branches.Contains(s.BranchId));
        }

        if (request.Status is { } status) query = query.Where(s => s.Status == status);
        if (request.OrderId is { } order) query = query.Where(s => s.SalesOrderId == order);
        if (request.CurrentAccountId is { } cari) query = query.Where(s => s.CurrentAccountId == cari);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(s => s.Number.ToLower().Contains(term) || db.CurrentAccounts.Any(c => c.Id == s.CurrentAccountId && c.Name.ToLower().Contains(term))
                                     || db.SalesOrders.Any(o => o.Id == s.SalesOrderId && o.Number.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var rows = await query.OrderByDescending(s => s.Created).ThenByDescending(s => s.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(s => new {
                                  s.Id, s.Number, s.Status, s.SalesOrderId,
                                  OrderNumber  = db.SalesOrders.Where(o => o.Id == s.SalesOrderId).Select(o => o.Number).FirstOrDefault(),
                                  s.CurrentAccountId,
                                  CustomerName = db.CurrentAccounts.Where(c => c.Id == s.CurrentAccountId).Select(c => c.Name).FirstOrDefault(),
                                  s.Date, s.InvoiceId
                              }).ToListAsync(ct);
        return new ShipmentListResult(rows.Select(r => new ShipmentRowDto(r.Id, r.Number, r.Status, r.SalesOrderId, r.OrderNumber ?? string.Empty, r.CurrentAccountId,
                                                                          r.CustomerName ?? string.Empty, r.Date, r.InvoiceId)).ToList(), total);
    }
}

[Authorize(Permission = SalesPermissions.Read)]
public sealed record GetShipmentQuery(Guid Id) : IRequest<ShipmentDto>;

public sealed class GetShipmentQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ShipmentMapper        mapper
) : IRequestHandler<GetShipmentQuery, ShipmentDto> {
    public async Task<ShipmentDto> Handle(GetShipmentQuery request, CancellationToken ct) {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, shipment.BranchId);
        return await mapper.ToDtoAsync(shipment, ct);
    }
}
