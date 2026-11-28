using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Sales.Orders;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Dispatch;

public sealed record DispatchInput(string? CarrierCode, string? TrackingNo, DateTimeOffset? DispatchAt, string? Vehicle, string? Driver);

/// <summary>
///     Paketlenmiş (ya da toplama-paketleme kapalıyken taslak) sevki çıkarır: stok çıkışı, irsaliye numarası, kargo bilgisi ve "shipment.shipped" olayı.
///     Açık miktar ve rezervasyon, sipariş kilidi altında yeniden denetlenir (P7 doğrudan sevki taslakları saymaz).
/// </summary>
[Authorize(Permission = ShippingPermissions.Ship)]
public sealed record ShipShipmentCommand(Guid Id, DispatchInput Input) : IRequest<ShipmentDto>;

public sealed class ShipShipmentCommandValidator : AbstractValidator<ShipShipmentCommand> {
    public ShipShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.CarrierCode).NotEmpty().WithMessage("Kargo firması seçilmelidir.").MaximumLength(30).WithMessage("Kargo firması kodu en fazla 30 karakter olabilir.");
        RuleFor(c => c.Input.TrackingNo).MaximumLength(100).WithMessage("Takip numarası en fazla 100 karakter olabilir.");
        RuleFor(c => c.Input.Vehicle).MaximumLength(100).WithMessage("Araç en fazla 100 karakter olabilir.");
        RuleFor(c => c.Input.Driver).MaximumLength(100).WithMessage("Sürücü en fazla 100 karakter olabilir.");
    }
}

public sealed class ShipShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    ShipmentService       shipments,
    ShipmentMapper        mapper
) : IRequestHandler<ShipShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(ShipShipmentCommand request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.SalesOrderId, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(head.SalesOrderId, ct)) throw new NotFoundException(head.SalesOrderId.ToString(), "SalesOrder");
            db.ClearTracking();
            var order    = await OrderRules.LoadAsync(db, tenant, head.SalesOrderId, true, ct);
            var shipment = await db.Shipments.Include(s => s.Lines).FirstAsync(s => s.Id == request.Id, ct);
            var shipping = await settings.GetAsync<ShippingSettings>(ct);

            // Yalnızca Packed (ya da kısayol açıkken Draft) çıkabilir; DeliveryFailed -> Shipped yeniden gönderimdir (resend).
            var current = shipment.Status.ToString();
            if (shipment.Status is not (ShipmentStatus.Draft or ShipmentStatus.Packed) || !ShipmentStatusMachine.CanMove(current, "Shipped", shipping.UsePickPack))
                throw ShippingErrors.InvalidTransition(current, "Shipped");

            var input   = request.Input;
            var carrier = CarrierRules.Resolve(shipping, input.CarrierCode);
            CarrierRules.EnsureTracking(carrier, input.TrackingNo);

            var lines = shipment.Lines.OrderBy(l => l.Sort).Select(l => new ShipmentLineInput(l.SalesOrderLineId, l.Quantity, l.Lots, l.Serials)).ToList();
            var ship  = new ShipmentInput(order.Id, shipment.WarehouseId, null, null, null, null, lines);
            var info = new DispatchInfo(carrier.Code, string.IsNullOrWhiteSpace(input.TrackingNo) ? null : input.TrackingNo.Trim(), (input.DispatchAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                                        string.IsNullOrWhiteSpace(input.Vehicle) ? null : input.Vehicle.Trim(), string.IsNullOrWhiteSpace(input.Driver) ? null : input.Driver.Trim());
            await shipments.ShipAsync(order, ship, ct, shipment, info);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

public sealed record IrsaliyePdfFile(byte[] Content, string FileName);

[Authorize(Permission = ShippingPermissions.Read)]
public sealed record GetIrsaliyePdfQuery(Guid Id) : IRequest<IrsaliyePdfFile>;

public sealed class GetIrsaliyePdfQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, IDocumentPdf pdf) : IRequestHandler<GetIrsaliyePdfQuery, IrsaliyePdfFile> {
    public async Task<IrsaliyePdfFile> Handle(GetIrsaliyePdfQuery request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.IrsaliyeNo, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);
        if (head.IrsaliyeNo is null) throw ShippingErrors.Conflict("irsaliye_not_issued", "Bu sevk için henüz irsaliye numarası verilmedi (sevk edilmemiş ya da e-İrsaliye kullanılıyor).");
        return new IrsaliyePdfFile(await pdf.RenderIrsaliyeAsync(request.Id, ct), $"{head.IrsaliyeNo}.pdf");
    }
}
