using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Sales.Recurring;

[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record ListRecurringQuery(bool? Active, Guid? CurrentAccountId) : IRequest<IReadOnlyList<RecurringListItem>>;

public sealed class ListRecurringQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    RecurringMapper       mapper
) : IRequestHandler<ListRecurringQuery, IReadOnlyList<RecurringListItem>> {
    public async Task<IReadOnlyList<RecurringListItem>> Handle(ListRecurringQuery request, CancellationToken ct) {
        var query = db.RecurringTemplates.AsNoTracking().Include(t => t.Lines).AsQueryable();
        if (tenant.BranchIds is { } branchList) {
            var branches = branchList.ToList();
            query = query.Where(t => branches.Contains(t.BranchId));
        }

        if (request.Active is { } active) query = query.Where(t => t.IsActive == active);
        if (request.CurrentAccountId is { } cari) query = query.Where(t => t.CurrentAccountId == cari);

        var templates = await query.OrderBy(t => t.Number).ToListAsync(ct);
        var ids       = templates.Select(t => t.Id).ToList();
        var cariIds   = templates.Select(t => t.CurrentAccountId).Distinct().ToList();
        var names     = await db.CurrentAccounts.AsNoTracking().Where(c => cariIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var runs      = (await db.RecurringRuns.AsNoTracking().Where(r => ids.Contains(r.RecurringTemplateId)).Select(r => new { r.RecurringTemplateId, r.Period }).ToListAsync(ct))
                        .GroupBy(r => r.RecurringTemplateId).ToDictionary(g => g.Key, g => g.Select(r => r.Period).ToList());
        var today = mapper.Today();
        return templates.Select(t => {
            var (next, last) = RecurringRules.Periods(t, runs.GetValueOrDefault(t.Id) ?? [], today);
            return new RecurringListItem(t.Id, t.Number, t.Name, t.CurrentAccountId, names.GetValueOrDefault(t.CurrentAccountId, string.Empty), t.Currency, t.Interval, t.DayOfMonth,
                                         t.StartDate, t.EndDate, t.Mode, t.IsActive, RecurringRules.Totals(t.Lines).Payable, next, last);
        }).ToList();
    }
}

[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record GetRecurringQuery(Guid Id) : IRequest<RecurringDto>;

public sealed class GetRecurringQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    RecurringMapper       mapper
) : IRequestHandler<GetRecurringQuery, RecurringDto> {
    public async Task<RecurringDto> Handle(GetRecurringQuery request, CancellationToken ct) {
        var template = await db.RecurringTemplates.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                       ?? throw new NotFoundException(request.Id.ToString(), "RecurringTemplate");
        BranchGuard.Ensure(tenant, template.BranchId);
        return await mapper.ToDtoAsync(template, ct);
    }
}

/// <summary>Şablonun ürettiği faturalar. Taslağı silinmiş bir dönemin faturası bulunamaz: satır "deleted" durumuyla döner (dönem yeniden üretilmez).</summary>
[Authorize(Permission = SalesPermissions.Recurring)]
public sealed record ListRecurringRunsQuery(Guid Id) : IRequest<IReadOnlyList<RecurringRunDto>>;

public sealed class ListRecurringRunsQueryHandler(
    IApplicationDbContext db,
    ICurrentTenant        tenant
) : IRequestHandler<ListRecurringRunsQuery, IReadOnlyList<RecurringRunDto>> {
    public async Task<IReadOnlyList<RecurringRunDto>> Handle(ListRecurringRunsQuery request, CancellationToken ct) {
        var branch = await db.RecurringTemplates.AsNoTracking().Where(t => t.Id == request.Id).Select(t => (Guid?)t.BranchId).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException(request.Id.ToString(), "RecurringTemplate");
        BranchGuard.Ensure(tenant, branch);

        var runs     = await db.RecurringRuns.AsNoTracking().Where(r => r.RecurringTemplateId == request.Id).OrderBy(r => r.Period).ToListAsync(ct);
        var ids      = runs.Select(r => r.InvoiceId).ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Number, i.Status, i.Payable }).ToDictionaryAsync(i => i.Id, ct);
        return runs.Select(r => invoices.TryGetValue(r.InvoiceId, out var i)
                               ? new RecurringRunDto(r.Id, r.Period, r.InvoiceId, i.Number, i.Status == InvoiceStatus.Draft ? "draft" : i.Status.ToString().ToLowerInvariant(), i.Payable, r.GeneratedAt)
                               : new RecurringRunDto(r.Id, r.Period, r.InvoiceId, null, "deleted", null, r.GeneratedAt)).ToList();
    }
}
