using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Plans;

[Authorize(Permission = ShippingPermissions.Read)]
[RequireModule(PlanRules.Module)]
public sealed record ListPlansQuery(DateOnly? Date, DeliveryPlanStatus? Status) : IRequest<IReadOnlyList<PlanRowDto>>;

public sealed class ListPlansQueryHandler(IApplicationDbContext db, ICurrentTenant tenant) : IRequestHandler<ListPlansQuery, IReadOnlyList<PlanRowDto>> {
    public async Task<IReadOnlyList<PlanRowDto>> Handle(ListPlansQuery request, CancellationToken ct) {
        var query = db.DeliveryPlans.AsNoTracking().AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(p => branches.Contains(p.BranchId));
        }

        if (request.Date is { } date) query = query.Where(p => p.Date == date);
        if (request.Status is { } status) query = query.Where(p => p.Status == status);
        var rows = await query.OrderByDescending(p => p.Date).ThenByDescending(p => p.Number).Take(200)
                              .Select(p => new PlanRowDto(p.Id, p.Number, p.Date, p.Vehicle, p.DriverName, p.Status, p.Stops.Count, p.Stops.Count(s => s.Status != StopStatus.Pending))).ToListAsync(ct);
        return rows;
    }
}

[Authorize(Permission = ShippingPermissions.Read)]
[RequireModule(PlanRules.Module)]
public sealed record GetPlanQuery(Guid Id) : IRequest<PlanDto>;

public sealed class GetPlanQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, PlanMapper mapper) : IRequestHandler<GetPlanQuery, PlanDto> {
    public async Task<PlanDto> Handle(GetPlanQuery request, CancellationToken ct) {
        var plan = await db.DeliveryPlans.AsNoTracking().Include(p => p.Stops).FirstOrDefaultAsync(p => p.Id == request.Id, ct) ?? throw new NotFoundException(request.Id.ToString(), "DeliveryPlan");
        BranchGuard.Ensure(tenant, plan.BranchId);
        return await mapper.ToDtoAsync(plan, ct);
    }
}

/// <summary>Rota föyü PDF'i: duraklar sırayla (adres, telefon, kalemler, imza satırı).</summary>
[Authorize(Permission = ShippingPermissions.Read)]
[RequireModule(PlanRules.Module)]
public sealed record GetPlanPdfQuery(Guid Id) : IRequest<PlanPdfFile>;

public sealed class GetPlanPdfQueryHandler(IApplicationDbContext db, ICurrentTenant tenant, IDocumentPdf pdf) : IRequestHandler<GetPlanPdfQuery, PlanPdfFile> {
    public async Task<PlanPdfFile> Handle(GetPlanPdfQuery request, CancellationToken ct) {
        var head = await db.DeliveryPlans.AsNoTracking().Where(p => p.Id == request.Id).Select(p => new { p.Number, p.BranchId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(request.Id.ToString(), "DeliveryPlan");
        BranchGuard.Ensure(tenant, head.BranchId);
        return new PlanPdfFile(await pdf.RenderDeliveryPlanAsync(request.Id, ct), $"{head.Number}.pdf");
    }
}
