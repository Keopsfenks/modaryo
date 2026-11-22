using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts;

namespace ERP.Application.Audit.Queries;

public sealed record ErpAuditEntryDto(
    Guid           Id,
    DateTimeOffset At,
    Guid?          ActorUserId,
    string         ActorLabel,
    string         Action,
    string         EntityType,
    string         EntityId,
    string?        Before,
    string?        After
);

[Authorize(Permission = AuditPermissions.Read)]
public sealed record ListAuditQuery(
    string? EntityType,
    int     Page     = 1,
    int     PageSize = 50
) : IRequest<PagedResult<ErpAuditEntryDto>>;

public sealed class ListAuditQueryValidator : AbstractValidator<ListAuditQuery> {
    public ListAuditQueryValidator() {
        RuleFor(q => q.Page).InclusiveBetween(1, 100000).WithMessage("Sayfa 1 ile 100000 arasında olmalıdır.");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 200).WithMessage("Sayfa boyutu 1 ile 200 arasında olmalıdır.");
    }
}

public sealed class ListAuditQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<ListAuditQuery, PagedResult<ErpAuditEntryDto>> {
    public async Task<PagedResult<ErpAuditEntryDto>> Handle(ListAuditQuery request, CancellationToken ct) {
        var query = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.EntityType)) query = query.Where(e => e.EntityType == request.EntityType);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
                        .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
                        .Select(e => new ErpAuditEntryDto(e.Id, e.At, e.ActorUserId, e.ActorLabel, e.Action,
                                                          e.EntityType, e.EntityId, e.Before, e.After))
                        .ToListAsync(ct);
        return new PagedResult<ErpAuditEntryDto>(items, total);
    }
}
