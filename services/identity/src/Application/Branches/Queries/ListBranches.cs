using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;

namespace IdentityService.Application.Branches.Queries;

/// <summary>Firmanın şubeleri: aktifler önce, sonra koda göre. Her üye görebilir.</summary>
[Authorize]
public record ListBranchesQuery : IRequest<IReadOnlyList<BranchDto>>;

public class ListBranchesQueryHandler(IApplicationDbContext db, CompanyAccess access) : IRequestHandler<ListBranchesQuery, IReadOnlyList<BranchDto>>
{
    public async Task<IReadOnlyList<BranchDto>> Handle(ListBranchesQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var branches = await db.Branches.AsNoTracking().Where(b => b.TenantId == tenantId).ToListAsync(ct);

        return branches
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.Code, StringComparer.Ordinal)
            .Select(BranchRules.ToDto)
            .ToList();
    }
}
