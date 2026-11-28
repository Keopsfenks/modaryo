using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Sales.Orders;
using ERP.Application.Sales.Shipments;

namespace ERP.Application.Shipping.Bulk;

public sealed record BulkShipInput(IReadOnlyList<Guid> OrderIds, Guid WarehouseId, DateOnly? Date);

public sealed record BulkShipError(string Code, string Message);

/// <summary>Sipariş başına sonuç: sevk açıldıysa ShipmentId, açılamadıysa Error (ikisi birlikte dolu olmaz).</summary>
public sealed record BulkShipRowDto(Guid OrderId, Guid? ShipmentId, string? ShipmentNumber, BulkShipError? Error);

public sealed record BulkShipResult(IReadOnlyList<BulkShipRowDto> Results, int Created, int Failed);

/// <summary>
///     Seçili siparişlerin açık miktarları için, tek depodan taslak sevk açar (ardından toplu toplama listesi: shipping/pick-lists/batch). Her sipariş kendi işleminde
///     ve kendi sipariş kilidiyle yürür; tek tek P7 kurallarına tabidir (onaylı olmalı, rezervasyon yeterli, açık miktarı aşamaz). Bir siparişin hatası yalnızca o siparişin
///     işlemini geri alır: yarım sevk kalmaz, diğer siparişler açılır ve hata sipariş bazında bildirilir.
/// </summary>
[Authorize(Permission = ShippingPermissions.Ship)]
public sealed record BulkShipCommand(BulkShipInput Input) : IRequest<BulkShipResult>;

public sealed class BulkShipCommandValidator : AbstractValidator<BulkShipCommand> {
    public const int MaxOrders = 100;

    public BulkShipCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.OrderIds).NotEmpty().WithMessage("En az bir sipariş seçilmelidir.").Must(ids => ids is null || ids.Count <= MaxOrders)
                                      .WithMessage($"Tek seferde en çok {MaxOrders} sipariş seçilebilir.");
        RuleFor(c => c.Input.WarehouseId).NotEmpty().WithMessage("Depo seçilmelidir.");
    }
}

public sealed class BulkShipCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ShipmentService       shipments
) : IRequestHandler<BulkShipCommand, BulkShipResult> {
    public async Task<BulkShipResult> Handle(BulkShipCommand request, CancellationToken ct) {
        var rows = new List<BulkShipRowDto>();
        foreach (var orderId in request.Input.OrderIds.Distinct()) {
            try {
                rows.Add(await ShipOneAsync(orderId, request.Input, ct));
            } catch (AppException e) {
                db.ClearTracking();
                rows.Add(new BulkShipRowDto(orderId, null, null, new BulkShipError(e.Code, e.Message)));
            } catch (NotFoundException) {
                db.ClearTracking();
                rows.Add(new BulkShipRowDto(orderId, null, null, new BulkShipError("not_found", "Sipariş ya da depo bulunamadı.")));
            } catch (ForbiddenAccessException) {
                db.ClearTracking();
                rows.Add(new BulkShipRowDto(orderId, null, null, new BulkShipError("forbidden", "Bu siparişin şubesine erişiminiz yok.")));
            } catch (DbUpdateConcurrencyException) {
                db.ClearTracking();
                rows.Add(new BulkShipRowDto(orderId, null, null, new BulkShipError("version_conflict", "Sipariş başkası tarafından değiştirildi; yeniden deneyin.")));
            }
        }

        return new BulkShipResult(rows, rows.Count(r => r.ShipmentId is not null), rows.Count(r => r.Error is not null));
    }

    private async Task<BulkShipRowDto> ShipOneAsync(Guid orderId, BulkShipInput input, CancellationToken ct) {
        var head = await OrderRules.LoadAsync(db, tenant, orderId, false, ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockSalesOrderAsync(head.Id, ct)) throw new NotFoundException(orderId.ToString(), "SalesOrder");
        db.ClearTracking();
        var order = await OrderRules.LoadAsync(db, tenant, head.Id, true, ct);
        var lines = await shipments.DraftableLinesAsync(order, ct);
        if (lines.Count == 0) throw ShippingErrors.Conflict("nothing_to_ship", "Siparişte sevk edilebilecek açık miktar yok.");

        var shipment = await shipments.CreateDraftAsync(order, new ShipmentInput(order.Id, input.WarehouseId, input.Date, null, null, null, lines, true), ct);
        await tx.CommitAsync(ct);
        db.ClearTracking();
        return new BulkShipRowDto(orderId, shipment.Id, shipment.Number, null);
    }
}
