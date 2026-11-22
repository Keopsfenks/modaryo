using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;

namespace ERP.Application.CurrentAccounts.Queries;

[Authorize(Permission = CariPermissions.Manage)]
public sealed record NextCodeQuery : IRequest<NextCodeDto>;

public sealed class NextCodeQueryHandler(
    IApplicationDbContext db
) : IRequestHandler<NextCodeQuery, NextCodeDto> {
    public async Task<NextCodeDto> Handle(NextCodeQuery request, CancellationToken ct) {
        var codes = await db.CurrentAccounts.AsNoTracking().Where(a => a.Code.StartsWith("C")).Select(a => a.Code)
                        .ToListAsync(ct);
        return new NextCodeDto(NextCodeCalculator.Next(codes));
    }
}