using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Sales.Shipments;
using ERP.Domain.Sales;

namespace ERP.Application.Shipping.Picking;

/// <summary>
///     Toplaması tamamlanmış (Picking) ya da paketlenmiş (Packed) sevki toplamayı geri alarak Draft'a döndürür: liste bağı ve koliler silinir, sevk yeniden
///     toplanabilir ya da iptal edilebilir. Stok hareketi yoktur. Durum başka ise 409 invalid_shipment_transition; faturaya bağlıysa 409 shipment_invoiced.
/// </summary>
[Authorize(Permission = ShippingPermissions.PickOrPack)]
public sealed record UnpickShipmentCommand(Guid Id) : IRequest<ShipmentDto>;

public sealed class UnpickShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ShipmentService       shipments,
    ShipmentMapper        mapper
) : IRequestHandler<UnpickShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(UnpickShipmentCommand request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.SalesOrderId, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(head.SalesOrderId, ct)) throw new NotFoundException(head.SalesOrderId.ToString(), "SalesOrder");
            db.ClearTracking();
            var shipment = await db.Shipments.Include(s => s.Lines).Include(s => s.Parcels).FirstAsync(s => s.Id == request.Id, ct);
            await shipments.UnpickAsync(shipment, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}
