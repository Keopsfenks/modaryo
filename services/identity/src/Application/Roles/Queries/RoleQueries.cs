using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;

namespace IdentityService.Application.Roles.Queries;

/// <summary>Firmanın rolleri: hazır roller sabit sırada (Sahip, Yönetici, Kullanıcı), sonra diğerleri. roller.read veya members.read gerekir.</summary>
[Authorize]
public record ListRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public class ListRolesQueryHandler(IApplicationDbContext db, CompanyAccess access, RoleRules rules)
    : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        rules.EnsureCanRead();
        var tenantId = access.TenantId;
        var roles = await db.TenantRoles.AsNoTracking().Include(r => r.Branches).Where(r => r.TenantId == tenantId).ToListAsync(ct);
        var counts = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .GroupBy(m => m.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        return roles
            .OrderBy(RoleRules.SystemRank)
            .ThenBy(r => r.Name, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
            .Select(r => RoleRules.ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }
}

[Authorize]
public record GetRoleQuery(Guid Id) : IRequest<RoleDto>;

public class GetRoleQueryHandler(RoleRules rules) : IRequestHandler<GetRoleQuery, RoleDto>
{
    public async Task<RoleDto> Handle(GetRoleQuery request, CancellationToken ct)
    {
        rules.EnsureCanRead();
        return await rules.ToDtoAsync(await rules.LoadAsync(request.Id, ct), ct);
    }
}
