using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Sales.Orders;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Shipments;

[Authorize(Permission = SalesPermissions.Ship)]
public sealed record CreateShipmentCommand(ShipmentInput Input) : IRequest<ShipmentDto>;

public sealed class CreateShipmentCommandValidator : AbstractValidator<CreateShipmentCommand> {
    public CreateShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.OrderId).NotEmpty().WithMessage("Sipariş seçilmelidir.");
        RuleFor(c => c.Input.Lines).NotEmpty().WithMessage("Sevk için en az bir satır seçilmelidir.");
        RuleForEach(c => c.Input.Lines).ChildRules(l => {
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Sevk miktarı sıfırdan büyük olmalıdır.");
        });
        RuleFor(c => c.Input.CargoNote).MaximumLength(500).WithMessage("Kargo notu en fazla 500 karakter olabilir.");
        RuleFor(c => c.Input.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
    }
}

public sealed class CreateShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ShipmentService       shipments,
    ShipmentMapper        mapper
) : IRequestHandler<CreateShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(CreateShipmentCommand request, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, request.Input.OrderId, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(head.Id.ToString(), "SalesOrder");
            db.ClearTracking();
            var order    = await OrderRules.LoadAsync(db, tenant, head.Id, true, ct);
            var shipment = await shipments.ShipAsync(order, request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = SalesPermissions.Ship)]
public sealed record DeliverShipmentCommand(Guid Id, DeliverInput Input) : IRequest<ShipmentDto>;

public sealed class DeliverShipmentCommandValidator : AbstractValidator<DeliverShipmentCommand> {
    public DeliverShipmentCommandValidator() {
        RuleFor(c => c.Input.ReceivedBy).MaximumLength(200).WithMessage("Teslim alan en fazla 200 karakter olabilir.");
    }
}

public sealed class DeliverShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IOutbox               outbox,
    TimeProvider          clock,
    ShipmentMapper        mapper
) : IRequestHandler<DeliverShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(DeliverShipmentCommand request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.SalesOrderId, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            // İptal ile aynı sipariş kilidi: teslim ve iptal birbirini aynı durum üzerinde görür.
            await db.LockSalesOrderAsync(head.SalesOrderId, ct);
            var shipment = await db.Shipments.Include(s => s.Lines).FirstAsync(s => s.Id == request.Id, ct);
            if (shipment.Status != ShipmentStatus.Shipped)
                throw SalesErrors.Conflict("shipment_not_deliverable", "Yalnızca sevk edilmiş sevkler teslim edildi olarak işaretlenebilir.");

            shipment.Status      = ShipmentStatus.Delivered;
            shipment.DeliveredAt = request.Input.DeliveredAt ?? clock.GetUtcNow();
            shipment.ReceivedBy  = string.IsNullOrWhiteSpace(request.Input.ReceivedBy) ? null : request.Input.ReceivedBy.Trim();
            outbox.Enqueue("sales.shipment-delivered", new { shipmentId = shipment.Id, number = shipment.Number, orderId = shipment.SalesOrderId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = SalesPermissions.Ship)]
public sealed record CancelShipmentCommand(Guid Id, string Reason) : IRequest<ShipmentDto>;

public sealed class CancelShipmentCommandValidator : AbstractValidator<CancelShipmentCommand> {
    public CancelShipmentCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İptal nedeni zorunludur.").Length(2, 200).WithMessage("İptal nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class CancelShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ShipmentService       shipments,
    ShipmentMapper        mapper
) : IRequestHandler<CancelShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(CancelShipmentCommand request, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == request.Id).Select(s => new { s.SalesOrderId, s.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            if (!await db.LockSalesOrderAsync(head.SalesOrderId, ct)) throw new NotFoundException(head.SalesOrderId.ToString(), "SalesOrder");
            db.ClearTracking();
            var order    = await OrderRules.LoadAsync(db, tenant, head.SalesOrderId, true, ct);
            var shipment = await db.Shipments.Include(s => s.Lines).FirstAsync(s => s.Id == request.Id, ct);
            await shipments.CancelAsync(order, shipment, request.Reason, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw SalesErrors.VersionConflict();
        }
    }
}
