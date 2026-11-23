using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Read)]
public sealed record GetDueItemsQuery(
    Guid AccountId
) : IRequest<IReadOnlyList<OpenItem>>;

public sealed class GetDueItemsQueryHandler(
    IApplicationDbContext db,
    OpenItemsReader       reader,
    TimeProvider          time
)
    : IRequestHandler<GetDueItemsQuery, IReadOnlyList<OpenItem>> {
    public async Task<IReadOnlyList<OpenItem>> Handle(GetDueItemsQuery request, CancellationToken ct) {
        if (!await db.CurrentAccounts.AsNoTracking().AnyAsync(a => a.Id == request.AccountId, ct))
            throw new NotFoundException(request.AccountId.ToString(), "CurrentAccount");

        return await reader.ForAccountAsync(request.AccountId, CurrentAccountClock.Today(time), ct);
    }
}