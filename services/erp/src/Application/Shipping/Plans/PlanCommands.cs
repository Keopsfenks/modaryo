using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Inventory.Documents;
using ERP.Application.Sales.Shipments;
using ERP.Application.Settings.Categories;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;
using ERP.Application.Shipping.Delivery;

namespace ERP.Application.Shipping.Plans;

/// <summary>
///     Teslimat planı: aynı şubeden, "Kendi aracımız" ile sevk edilmiş (Shipped) sevklerin sıralı durak listesi. Her durak sevkin teslimat adresinin kopyasını taşır.
///     Rota optimizasyonu kapsam dışıdır; sıra elle verilir (PUT stops).
/// </summary>
[Authorize(Permission = ShippingPermissions.Plan)]
[RequireModule(PlanRules.Module)]
public sealed record CreatePlanCommand(PlanInput Input) : IRequest<PlanDto>;

public sealed class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand> {
    public const int MaxStops = 100;

    public CreatePlanCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Date).NotEqual(default(DateOnly)).WithMessage("Plan tarihi zorunludur.");
        RuleFor(c => c.Input.Vehicle).MaximumLength(100).WithMessage("Araç en fazla 100 karakter olabilir.");
        RuleFor(c => c.Input.DriverName).MaximumLength(100).WithMessage("Sürücü adı en fazla 100 karakter olabilir.");
        RuleFor(c => c.Input.ShipmentIds).NotEmpty().WithMessage("Plana en az bir sevk eklenmelidir.")
                                         .Must(ids => ids is null || ids.Count <= MaxStops).WithMessage($"Bir plana en çok {MaxStops} sevk eklenebilir.")
                                         .Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("Aynı sevk bir planda yalnızca bir kez yer alabilir.");
    }
}

public sealed class CreatePlanCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IMemberDirectory      members,
    PlanMapper            mapper
) : IRequestHandler<CreatePlanCommand, PlanDto> {
    public async Task<PlanDto> Handle(CreatePlanCommand request, CancellationToken ct) {
        var input    = request.Input;
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        string? driverName = PlanMapper.Clean(input.DriverName);
        if (input.DriverUserId is { } userId) {
            var member = await members.GetAsync(tenantId, userId, ct) ?? throw ShippingErrors.Bad("driver_invalid", "Sürücü bu firmanın üyesi değil.");
            driverName ??= member.FullName;
        }

        var heads = await db.Shipments.AsNoTracking().Where(s => input.ShipmentIds.Contains(s.Id)).Select(s => new { s.Id, s.SalesOrderId, s.BranchId }).ToListAsync(ct);
        if (heads.Count != input.ShipmentIds.Count) throw new NotFoundException(string.Join(",", input.ShipmentIds.Except(heads.Select(h => h.Id))), "Shipment");
        foreach (var branch in heads.Select(h => h.BranchId).Distinct()) BranchGuard.Ensure(tenant, branch);
        if (heads.Select(h => h.BranchId).Distinct().Count() > 1) throw ShippingErrors.Bad("plan_branch_mismatch", "Plandaki sevkler aynı şubeye ait olmalıdır.");

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            // Sevklerin durumu sabit kalsın diye siparişleri kimlik sırasıyla kilitleriz (teslim/iptal akışlarıyla aynı kilit).
            foreach (var orderId in heads.Select(h => h.SalesOrderId).Distinct().Order()) await db.LockSalesOrderAsync(orderId, ct);
            db.ClearTracking();

            var shipments = await db.Shipments.AsNoTracking().Where(s => input.ShipmentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            foreach (var shipment in shipments.Values)
                if (shipment.Status != ShipmentStatus.Shipped || shipment.CarrierCode != ShippingSettings.OwnVehicleCode) throw PlanRules.StopNotShipped();

            var planned = db.DeliveryStops.Where(st => input.ShipmentIds.Contains(st.ShipmentId) && st.Status == StopStatus.Pending)
                            .Join(db.DeliveryPlans.Where(p => p.Status != DeliveryPlanStatus.Cancelled), st => st.DeliveryPlanId, p => p.Id, (st, _) => st.Id);
            if (await planned.AnyAsync(ct)) throw ShippingErrors.Conflict("plan_stop_duplicate", "Sevklerden biri başka bir planda bekleyen durak olarak yer alıyor.");

            var date = input.Date;
            var plan = new DeliveryPlan {
                Number = await DocumentNumbers.NextAsync(db, "TP", date, ct), BranchId = heads[0].BranchId, Date = date, Vehicle = PlanMapper.Clean(input.Vehicle),
                DriverUserId = input.DriverUserId, DriverName = driverName
            };
            var sort = 1;
            foreach (var id in input.ShipmentIds) {
                var shipment = shipments[id];
                plan.Stops.Add(new DeliveryStop { DeliveryPlanId = plan.Id, Sort = sort++, ShipmentId = id, Address = shipment.DeliveryAddress is { } a ? a with { } : null });
            }

            db.DeliveryPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(plan, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

[Authorize(Permission = ShippingPermissions.Plan)]
[RequireModule(PlanRules.Module)]
public sealed record ReorderStopsCommand(Guid Id, StopOrderInput Input) : IRequest<PlanDto>;

public sealed class ReorderStopsCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    PlanMapper            mapper
) : IRequestHandler<ReorderStopsCommand, PlanDto> {
    public async Task<PlanDto> Handle(ReorderStopsCommand request, CancellationToken ct) {
        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            var plan = await db.DeliveryPlans.Include(p => p.Stops).FirstOrDefaultAsync(p => p.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "DeliveryPlan");
            BranchGuard.Ensure(tenant, plan.BranchId);
            if (plan.Status is DeliveryPlanStatus.Completed or DeliveryPlanStatus.Cancelled) throw ShippingErrors.Conflict("plan_closed", "Tamamlanmış ya da iptal edilmiş planın sırası değiştirilemez.");

            var ids = request.Input.StopIds ?? [];
            if (ids.Count != plan.Stops.Count || ids.Distinct().Count() != ids.Count || ids.Except(plan.Stops.Select(s => s.Id)).Any())
                throw ShippingErrors.Bad("stops_mismatch", "Sıra, planın tüm duraklarını tam olarak bir kez içermelidir.");

            // (plan, sıra) benzersiz dizini: önce geçici negatif değerlere, sonra son değerlere yazılır.
            var byId = plan.Stops.ToDictionary(s => s.Id);
            for (var i = 0; i < ids.Count; i++) byId[ids[i]].Sort = -(i + 1);
            await db.SaveChangesAsync(ct);
            for (var i = 0; i < ids.Count; i++) byId[ids[i]].Sort = i + 1;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(plan, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

/// <summary>Durağı teslim edildi yapar: Task 3 teslim akışı (kanıtlı) aynı işlemde çalışır; sevk Delivered olur.</summary>
[Authorize(Permission = ShippingPermissions.Deliver)]
[RequireModule(PlanRules.Module)]
public sealed record DeliverStopCommand(Guid PlanId, Guid StopId, DeliverInput Input) : IRequest<PlanDto>;

public sealed class DeliverStopCommandValidator : AbstractValidator<DeliverStopCommand> {
    public DeliverStopCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.ReceivedBy).NotEmpty().WithMessage("Teslim alan zorunludur.").Must(r => r is null || r.Trim().Length is >= 2 and <= 100)
                                        .WithMessage("Teslim alan 2 ile 100 karakter arasında olmalıdır.");
        RuleFor(c => c.Input.Note).MaximumLength(500).WithMessage("Not en fazla 500 karakter olabilir.");
    }
}

/// <summary>Durağı başarısız yapar: Task 3 başarısız teslimat akışı aynı işlemde çalışır; sevk DeliveryFailed olur (sonra yeniden gönderilebilir ya da iade edilir).</summary>
[Authorize(Permission = ShippingPermissions.Deliver)]
[RequireModule(PlanRules.Module)]
public sealed record FailStopCommand(Guid PlanId, Guid StopId, FailInput Input) : IRequest<PlanDto>;

public sealed class FailStopCommandValidator : AbstractValidator<FailStopCommand> {
    public FailStopCommandValidator() {
        RuleFor(c => c.Input).NotNull();
        RuleFor(c => c.Input.Reason).NotEmpty().WithMessage("Başarısızlık nedeni zorunludur.").Must(r => r is null || r.Trim().Length is >= 2 and <= 500)
                                    .WithMessage("Başarısızlık nedeni 2 ile 500 karakter arasında olmalıdır.");
    }
}

/// <summary>Durak sonuçlandırma iskeleti: sevkin siparişini kilitler, durağı ve sevki yükler, akışı çalıştırır, durağı ve plan durumunu günceller.</summary>
internal static class StopFlow {
    public static async Task<PlanDto> RunAsync(IApplicationDbContext db, ICurrentTenant tenant, DeliveryService delivery, PlanMapper mapper, Guid planId, Guid stopId, StopStatus outcome,
                                               Func<Shipment, Task> action, string? note, CancellationToken ct) {
        var head = await db.DeliveryStops.AsNoTracking().Where(s => s.Id == stopId && s.DeliveryPlanId == planId)
                           .Join(db.Shipments, s => s.ShipmentId, sh => sh.Id, (s, sh) => new { sh.Id, sh.SalesOrderId, sh.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(stopId.ToString(), "DeliveryStop");
        BranchGuard.Ensure(tenant, head.BranchId);

        await using var tx = await db.BeginTransactionAsync(ct);
        try {
            await db.LockSalesOrderAsync(head.SalesOrderId, ct);
            db.ClearTracking();
            var plan = await db.DeliveryPlans.Include(p => p.Stops).FirstAsync(p => p.Id == planId, ct);
            var stop = plan.Stops.First(s => s.Id == stopId);
            if (plan.Status is DeliveryPlanStatus.Cancelled) throw ShippingErrors.Conflict("plan_closed", "İptal edilmiş planın durağı sonuçlandırılamaz.");
            if (stop.Status != StopStatus.Pending) throw ShippingErrors.Conflict("plan_stop_closed", "Durak zaten sonuçlandırılmış.");

            await action(await delivery.LoadAsync(head.Id, ct));
            stop.Status = outcome;
            stop.Note   = PlanMapper.Clean(note);
            PlanRules.Refresh(plan);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await mapper.ToDtoAsync(plan, ct);
        } catch (DbUpdateConcurrencyException) {
            db.ClearTracking();
            throw ShippingErrors.VersionConflict();
        }
    }
}

public sealed class DeliverStopCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    DeliveryService       delivery,
    PlanMapper            mapper
) : IRequestHandler<DeliverStopCommand, PlanDto> {
    public Task<PlanDto> Handle(DeliverStopCommand request, CancellationToken ct) {
        return StopFlow.RunAsync(db, tenant, delivery, mapper, request.PlanId, request.StopId, StopStatus.Delivered, shipment => delivery.DeliverAsync(shipment, request.Input, ct),
                                 request.Input.Note, ct);
    }
}

public sealed class FailStopCommandHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    DeliveryService       delivery,
    PlanMapper            mapper
) : IRequestHandler<FailStopCommand, PlanDto> {
    public Task<PlanDto> Handle(FailStopCommand request, CancellationToken ct) {
        return StopFlow.RunAsync(db, tenant, delivery, mapper, request.PlanId, request.StopId, StopStatus.Failed, shipment => delivery.FailAsync(shipment, request.Input, ct),
                                 request.Input.Reason, ct);
    }
}
