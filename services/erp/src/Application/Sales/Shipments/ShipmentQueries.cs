using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Shipping;
using ERP.Application.Shipping.Dispatch;
using ERP.Domain.Catalog;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Shipments;

public sealed class ShipmentMapper(IApplicationDbContext db, ITenantSettings settings) {
    public async Task<ShipmentDto> ToDtoAsync(Shipment s, CancellationToken ct) {
        var order    = await db.SalesOrders.AsNoTracking().Where(o => o.Id == s.SalesOrderId).Select(o => o.Number).FirstOrDefaultAsync(ct);
        var cari     = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == s.CurrentAccountId).Select(c => new { c.Name, c.Email }).FirstOrDefaultAsync(ct);
        var customer = cari?.Name;
        var shipping = await settings.GetAsync<ShippingSettings>(ct);
        var proof = await db.DeliveryProofs.AsNoTracking().Where(p => p.ShipmentId == s.Id).OrderByDescending(p => p.DeliveredAt).FirstOrDefaultAsync(ct);
        var productIds = s.Lines.Select(l => l.ProductId).Distinct().ToList();
        var services   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id) && p.Kind == ProductKind.Service).Select(p => p.Id).ToListAsync(ct);
        var lines = s.Lines.OrderBy(l => l.Sort)
                     .Select(l => new ShipmentLineDto(l.Id, l.Sort, l.SalesOrderLineId, l.ProductId, l.VariantId, l.Description, l.Unit, l.Factor, l.Quantity, l.Width, l.Length,
                                                      l.InvoicedQuantity, l.Lots, l.Serials, services.Contains(l.ProductId))).ToList();
        var parcels = (await db.Parcels.AsNoTracking().Where(p => p.ShipmentId == s.Id).OrderBy(p => p.Index).ToListAsync(ct))
                      .Select(p => new ParcelDto(p.Id, p.Index, p.WeightKg, p.WidthCm, p.LengthCm, p.HeightCm, p.Desi, p.Contents.Select(c => new ParcelContentDto(c.ShipmentLineId, c.Quantity)).ToList())).ToList();
        var attempts = (await db.DeliveryAttempts.AsNoTracking().Where(a => a.ShipmentId == s.Id).OrderBy(a => a.AttemptNo).ToListAsync(ct))
                       .Select(a => new AttemptDto(a.AttemptNo, a.SentAt, a.Outcome.ToString(), a.ResolvedAt, a.FailureReason)).ToList();
        return new ShipmentDto(s.Id, s.Number, s.Status, s.SalesOrderId, order ?? string.Empty, s.CurrentAccountId, customer ?? string.Empty, s.BranchId, s.WarehouseId, s.Date,
                               s.ShippedAt, s.DeliveredAt, s.ReceivedBy, s.DeliveryAddress, s.CargoNote, s.Notes, s.CancelReason, s.InvoiceId, s.Version, lines,
                               s.CarrierCode, s.TrackingNo, s.IrsaliyeNo, s.DispatchAt, s.Vehicle, s.Driver, s.PickListId, parcels.Sum(p => p.WeightKg), parcels.Sum(p => p.Desi), parcels, attempts,
                               CarrierRules.Name(shipping, s.CarrierCode), CarrierRules.TrackingUrl(shipping, s.CarrierCode, s.TrackingNo), string.IsNullOrWhiteSpace(cari?.Email), s.ReturnId,
                               proof is null ? null : new ProofDto(proof.ReceivedBy, proof.DeliveredAt, proof.PhotoFileId, proof.Note));
    }
}

[Authorize(Permission = ShippingPermissions.ReadOrSalesRead)]
public sealed record ListShipmentsQuery(
    ShipmentStatus? Status,
    Guid?           OrderId,
    Guid?           CurrentAccountId,
    string?         Search,
    int             Page          = 1,
    int             PageSize      = 25,
    string?         Statuses      = null,
    DateTimeOffset? DeliveredFrom = null
) : IRequest<ShipmentListResult>;

public sealed class ListShipmentsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings
) : IRequestHandler<ListShipmentsQuery, ShipmentListResult> {
    public async Task<ShipmentListResult> Handle(ListShipmentsQuery request, CancellationToken ct) {
        var query = db.Shipments.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(s => branches.Contains(s.BranchId));
        }

        if (request.Status is { } status) query = query.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Statuses)) {
            var wanted = new List<ShipmentStatus>();
            foreach (var part in request.Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (!Enum.TryParse<ShipmentStatus>(part, true, out var parsed) || !Enum.IsDefined(parsed))
                    throw ShippingErrors.Bad("status_invalid", $"Geçersiz sevk durumu: \"{part}\".");
                wanted.Add(parsed);
            }

            query = query.Where(s => wanted.Contains(s.Status));
        }

        if (request.DeliveredFrom is { } deliveredFrom) query = query.Where(s => s.DeliveredAt != null && s.DeliveredAt >= deliveredFrom);
        if (request.OrderId is { } order) query = query.Where(s => s.SalesOrderId == order);
        if (request.CurrentAccountId is { } cari) query = query.Where(s => s.CurrentAccountId == cari);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(s => s.Number.ToLower().Contains(term) || db.CurrentAccounts.Any(c => c.Id == s.CurrentAccountId && c.Name.ToLower().Contains(term))
                                     || db.SalesOrders.Any(o => o.Id == s.SalesOrderId && o.Number.ToLower().Contains(term)));
        }

        var total    = await query.CountAsync(ct);
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var rows = await query.OrderByDescending(s => s.Created).ThenByDescending(s => s.Number).Skip((page - 1) * pageSize).Take(pageSize)
                              .Select(s => new {
                                  s.Id, s.Number, s.Status, s.SalesOrderId,
                                  OrderNumber   = db.SalesOrders.Where(o => o.Id == s.SalesOrderId).Select(o => o.Number).FirstOrDefault(),
                                  s.CurrentAccountId,
                                  CustomerName  = db.CurrentAccounts.Where(c => c.Id == s.CurrentAccountId).Select(c => c.Name).FirstOrDefault(),
                                  CustomerEmail = db.CurrentAccounts.Where(c => c.Id == s.CurrentAccountId).Select(c => c.Email).FirstOrDefault(),
                                  s.Date, s.InvoiceId, s.ShippedAt, s.DeliveredAt, s.CarrierCode, s.TrackingNo, s.IrsaliyeNo, s.PickListId, s.Created, s.BranchId
                              }).ToListAsync(ct);

        // Pano alanları sayfadaki sevkler için toplu sorgularla (kalemler, koli sayıları) ve tek ayar okumasıyla gelir; sevk başına sorgu yoktur.
        var ids = rows.Select(r => r.Id).ToList();
        var lines = (await db.ShipmentLines.AsNoTracking().Where(l => ids.Contains(l.ShipmentId)).Select(l => new { l.ShipmentId, l.Sort, l.ProductId, l.Description, l.Unit, l.Quantity }).ToListAsync(ct))
                    .GroupBy(l => l.ShipmentId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Sort).ToList());
        var parcels = await db.Parcels.AsNoTracking().Where(p => ids.Contains(p.ShipmentId)).GroupBy(p => p.ShipmentId).Select(g => new { g.Key, Count = g.Count() })
                              .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var productIds = lines.Values.SelectMany(l => l).Select(l => l.ProductId).Distinct().ToList();
        var services   = (await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id) && p.Kind == ProductKind.Service).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var shipping   = await settings.GetAsync<ShippingSettings>(ct);

        var items = rows.Select(r => {
            var own = lines.GetValueOrDefault(r.Id) ?? [];
            return new ShipmentRowDto(r.Id, r.Number, r.Status, r.SalesOrderId, r.OrderNumber ?? string.Empty, r.CurrentAccountId, r.CustomerName ?? string.Empty, r.Date, r.InvoiceId,
                                      r.ShippedAt, r.DeliveredAt, r.CarrierCode, CarrierRules.Name(shipping, r.CarrierCode), r.TrackingNo, CarrierRules.TrackingUrl(shipping, r.CarrierCode, r.TrackingNo),
                                      r.IrsaliyeNo, r.PickListId, own.Count, parcels.GetValueOrDefault(r.Id),
                                      own.Take(3).Select(l => new ShipmentRowItemDto(l.Description, l.Unit, l.Quantity, services.Contains(l.ProductId))).ToList(),
                                      string.IsNullOrWhiteSpace(r.CustomerEmail), r.Created, r.BranchId);
        }).ToList();
        return new ShipmentListResult(items, total);
    }
}

[Authorize(Permission = ShippingPermissions.ReadOrSalesRead)]
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
