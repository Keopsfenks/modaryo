using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Read)]
public sealed record GetCurrentAccountQuery(Guid Id) : IRequest<CurrentAccountDto>;

public sealed class GetCurrentAccountQueryHandler(IApplicationDbContext db, TimeProvider time)
    : IRequestHandler<GetCurrentAccountQuery, CurrentAccountDto>
{
    public Task<CurrentAccountDto> Handle(GetCurrentAccountQuery request, CancellationToken ct) =>
        CurrentAccountReader.GetDtoAsync(db, request.Id, CurrentAccountClock.Today(time), ct);
}
