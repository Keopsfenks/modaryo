using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Sales;
using ERP.Application.Sales.Invoicing;
using ERP.Application.Sales.Orders;
using ERP.Application.Sales.Returns;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Delivery;

/// <summary>Sevkin siparişini kilitleyip sevki (Lines + Attempts) izlenir yükleyen ortak iskelet; her teslimat işlemi aynı sipariş kilidini alır.</summary>
internal static class DeliveryFlow {
    public static async Task<(Guid OrderId, Guid BranchId, Guid CariId)> HeadAsync(IApplicationDbContext db, ICurrentTenant tenant, Guid id, CancellationToken ct) {
        var head = await db.Shipments.AsNoTracking().Where(s => s.Id == id).Select(s => new { s.SalesOrderId, s.BranchId, s.CurrentAccountId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(id.ToString(), "Shipment");
        BranchGuard.Ensure(tenant, head.BranchId);
        return (head.SalesOrderId, head.BranchId, head.CurrentAccountId);
    }
}

[Authorize(Permission = ShippingPermissions.Deliver)]
public sealed record DeliverShipmentCommand(Guid Id, DeliverInput Input) : IRequest<ShipmentDto>;

public sealed class DeliverShipmentCommandValidator : AbstractValidator<DeliverShipmentCommand> {
    public DeliverShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceivedBy).NotEmpty().WithMessage("Teslim alan zorunludur.").Must(r => r is null || r.Trim().Length is >= 2 and <= 100)
                                        .WithMessage("Teslim alan 2 ile 100 karakter arasında olmalıdır.");
        RuleFor(c => c.Input.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

public sealed class DeliverShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    DeliveryService       delivery,
    ShipmentMapper        mapper
) : IRequestHandler<DeliverShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(DeliverShipmentCommand request, CancellationToken ct) {
        var head = await DeliveryFlow.HeadAsync(db, tenant, request.Id, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            // İptal ile aynı sipariş kilidi: teslim ve iptal birbirini aynı durum üzerinde görür.
            await db.LockSalesOrderAsync(head.OrderId, ct);
            db.ClearTracking();
            var shipment = await delivery.LoadAsync(request.Id, ct);
            await delivery.DeliverAsync(shipment, request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Deliver)]
public sealed record FailShipmentCommand(Guid Id, FailInput Input) : IRequest<ShipmentDto>;

public sealed class FailShipmentCommandValidator : AbstractValidator<FailShipmentCommand> {
    public FailShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Reason).NotEmpty().WithMessage("Başarısızlık nedeni zorunludur.").Must(r => r is null || r.Trim().Length is >= 2 and <= 500)
                                    .WithMessage("Başarısızlık nedeni 2 ile 500 karakter arasında olmalıdır.");
    }
}

public sealed class FailShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    DeliveryService       delivery,
    ShipmentMapper        mapper
) : IRequestHandler<FailShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(FailShipmentCommand request, CancellationToken ct) {
        var head = await DeliveryFlow.HeadAsync(db, tenant, request.Id, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockSalesOrderAsync(head.OrderId, ct);
            db.ClearTracking();
            var shipment = await delivery.LoadAsync(request.Id, ct);
            await delivery.FailAsync(shipment, request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Ship)]
public sealed record ResendShipmentCommand(Guid Id, ResendInput Input) : IRequest<ShipmentDto>;

public sealed class ResendShipmentCommandValidator : AbstractValidator<ResendShipmentCommand> {
    public ResendShipmentCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.CarrierCode).MaximumLength(30).WithMessage("Kargo firması kodu en fazla 30 karakter olabilir.");
        RuleFor(c => c.Input.TrackingNo).MaximumLength(100).WithMessage("Takip numarası en fazla 100 karakter olabilir.");
    }
}

public sealed class ResendShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ITenantSettings       settings,
    DeliveryService       delivery,
    ShipmentMapper        mapper
) : IRequestHandler<ResendShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(ResendShipmentCommand request, CancellationToken ct) {
        var head = await DeliveryFlow.HeadAsync(db, tenant, request.Id, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockSalesOrderAsync(head.OrderId, ct);
            db.ClearTracking();
            var shipment = await delivery.LoadAsync(request.Id, ct);
            await delivery.ResendAsync(shipment, await settings.GetAsync<ShippingSettings>(ct), request.Input, ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

/// <summary>
///     Teslim edilemeyen sevkin iadesi. Faturasızsa stok çıkışı geri alınır ve sipariş satırı yeniden açılır (P7 iptal yolu); tamamen faturalıysa (kesilmiş fatura)
///     P7 iade belgesi (satis.return akışı: stok girişi + iade taslak faturası) aynı işlemde kesilir. Kesilmemiş taslak fatura (shipment_invoice_draft) ya da kısmi
///     faturalama (shipment_partially_invoiced) karışık durumdur ve desteklenmez; önce fatura düzeltilmelidir. Kilit sırası: cari, sipariş, sıra sayaçları, stok.
/// </summary>
[Authorize(Permission = ShippingPermissions.Ship)]
public sealed record ReturnShipmentCommand(Guid Id, string Reason) : IRequest<ShipmentDto>;

public sealed class ReturnShipmentCommandValidator : AbstractValidator<ReturnShipmentCommand> {
    public ReturnShipmentCommandValidator() {
        RuleFor(c => c.Reason).NotEmpty().WithMessage("İade nedeni zorunludur.").Length(2, 200).WithMessage("İade nedeni 2 ile 200 karakter arasında olmalıdır.");
    }
}

public sealed class ReturnShipmentCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IOutbox               outbox,
    ShipmentService       shipments,
    ReturnCreator         returns,
    DeliveryService       delivery,
    ShipmentMapper        mapper
) : IRequestHandler<ReturnShipmentCommand, ShipmentDto> {
    public async Task<ShipmentDto> Handle(ReturnShipmentCommand request, CancellationToken ct) {
        var head = await DeliveryFlow.HeadAsync(db, tenant, request.Id, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockCurrentAccountAsync(head.CariId, ct);
            if (!await db.LockSalesOrderAsync(head.OrderId, ct)) throw new NotFoundException(head.OrderId.ToString(), "SalesOrder");
            db.ClearTracking();
            var order    = await OrderRules.LoadAsync(db, tenant, head.OrderId, true, ct);
            var shipment = await delivery.LoadAsync(request.Id, ct);
            if (shipment.Status != ShipmentStatus.DeliveryFailed) throw ShippingErrors.InvalidTransition(shipment.Status.ToString(), "Returned");

            var lineIds = shipment.Lines.Select(l => l.Id).ToList();
            var links = await db.InvoiceLines.AsNoTracking().Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value))
                                .Join(db.Invoices.Where(i => i.SourceType == ShipmentInvoiceLinks.Source && i.Status != InvoiceStatus.Cancelled), l => l.InvoiceId, i => i.Id,
                                      (l, i) => new { Line = l, i.Status }).ToListAsync(ct);
            if (links.Count == 0) {
                await shipments.ReturnUninvoicedAsync(order, shipment, request.Reason, ct);
            } else {
                // Faturalı iade P7 iade belgesi (stok girişi + iade faturası) keser: sevkiyat.ship yetmez, satis.return da gerekir (yalnızca bu dalda; faturasız iade değil).
                if (!session.HasPermission(SalesPermissions.Return)) throw new ForbiddenAccessException();
                if (links.Any(x => x.Status == InvoiceStatus.Draft))
                    throw ShippingErrors.Conflict("shipment_invoice_draft", "Sevke bağlı kesilmemiş taslak fatura var; önce taslağı silin ya da faturayı kesin.");
                foreach (var line in shipment.Lines) {
                    var invoiced = links.Where(x => x.Line.SourceLineId == line.Id).Sum(x => x.Line.Quantity * x.Line.Factor);
                    if (invoiced < line.Quantity * line.Factor)
                        throw ShippingErrors.Conflict("shipment_partially_invoiced", "Sevk kısmen faturalanmış; iade için önce faturaları sevkin tamamını kapsayacak şekilde düzeltin.");
                }

                var kind = await db.Warehouses.AsNoTracking().Where(w => w.Id == shipment.WarehouseId).Select(w => w.Kind).FirstAsync(ct);
                Guid? firstReturn = null;
                foreach (var invoice in links.GroupBy(x => x.Line.InvoiceId).OrderBy(g => g.Key)) {
                    var lines = invoice.Select(x => new ReturnLineInput(x.Line.Id, x.Line.Quantity, x.Line.Lots, x.Line.Serials)).ToList();
                    var ret   = await returns.CreateAsync(new ReturnInput(invoice.Key, kind, shipment.WarehouseId, null, request.Reason.Trim(), lines), ct);
                    firstReturn ??= ret.Id;
                }

                shipment.Status       = ShipmentStatus.Returned;
                shipment.CancelReason = request.Reason.Trim();
                shipment.ReturnId     = firstReturn;
                outbox.Enqueue("shipment.returned", new { shipmentId = shipment.Id, number = shipment.Number, orderId = order.Id, reason = shipment.CancelReason, returnId = firstReturn });
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(shipment, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}
