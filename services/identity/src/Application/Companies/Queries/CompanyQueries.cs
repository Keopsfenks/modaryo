using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.Companies.Queries;

[Authorize]
public record GetCurrentCompanyQuery : IRequest<CompanyDto>;

public class GetCurrentCompanyQueryHandler(IApplicationDbContext db, CompanyAccess access) : IRequestHandler<GetCurrentCompanyQuery, CompanyDto>
{
    public async Task<CompanyDto> Handle(GetCurrentCompanyQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException(tenantId.ToString(), "Company");
        return new CompanyDto(tenant.Id, tenant.Name, tenant.CreatedAt);
    }
}

/// <summary>Davet formundaki rol listesi: hazır roller sabit sırada (Sahip, Yönetici, Kullanıcı), sonra diğerleri.</summary>
[Authorize(Permission = Permissions.MembersRead)]
public record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public class GetRolesQueryHandler(IApplicationDbContext db, CompanyAccess access) : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private static readonly string[] SystemOrder = [SystemRoles.Owner, SystemRoles.Admin, SystemRoles.Member];

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var roles = await db.TenantRoles.AsNoTracking().Where(r => r.TenantId == tenantId).ToListAsync(ct);
        return roles
            .OrderBy(r => r.SystemKey is null ? int.MaxValue : Array.IndexOf(SystemOrder, r.SystemKey))
            .ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.SystemKey))
            .ToList();
    }
}

[Authorize(Permission = Permissions.MembersRead)]
public record GetMembersQuery : IRequest<IReadOnlyList<MemberDto>>;

public class GetMembersQueryHandler(IApplicationDbContext db, CompanyAccess access, IUserAccounts users)
    : IRequestHandler<GetMembersQuery, IReadOnlyList<MemberDto>>
{
    public async Task<IReadOnlyList<MemberDto>> Handle(GetMembersQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var memberships = await db.Memberships.AsNoTracking()
            .Include(m => m.Role)
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);
        var people = await users.FindManyAsync(memberships.Select(m => m.UserId), ct);

        return memberships
            .Where(m => people.ContainsKey(m.UserId))
            .Select(m => new MemberDto(m.UserId, people[m.UserId].Email, people[m.UserId].FullName,
                m.RoleId, m.Role.Name, m.Role.SystemKey, m.JoinedAt))
            .ToList();
    }
}
