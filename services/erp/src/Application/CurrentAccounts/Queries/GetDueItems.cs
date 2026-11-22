using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Read)]
public sealed record GetDueItemsQuery(
    Guid AccountId
) : IRequest<IReadOnlyList<OpenItem>>;

public sealed class GetDueItemsQueryHandler(
    IApplicationDbContext db,
    TimeProvider          time
)
    : IRequestHandler<GetDueItemsQuery, IReadOnlyList<OpenItem>> {
    public async Task<IReadOnlyList<OpenItem>> Handle(GetDueItemsQuery request, CancellationToken ct) {
        if (!await db.CurrentAccounts.AsNoTracking().AnyAsync(a => a.Id == request.AccountId, ct))
            throw new NotFoundException(request.AccountId.ToString(), "CurrentAccount");

        var entries = await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == request.AccountId)
                          .ToListAsync(ct);
        return DueMatching.OpenItems(entries, CurrentAccountClock.Today(time));
    }
}