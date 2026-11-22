using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.PermissionCatalogs;

public sealed record PermissionDto(string Code, string Name, string Description);

public sealed record PermissionGroupDto(string Group, string? ModuleKey, IReadOnlyList<PermissionDto> Permissions);

public sealed record PermissionCatalogDto(IReadOnlyList<PermissionGroupDto> Groups);

/// <summary>Rol düzenleyicinin izin listesi: firmada kapalı modüllerin izinleri gizlenir.</summary>
[Authorize(Permission = Permissions.RolesRead)]
public record GetPermissionCatalogQuery : IRequest<PermissionCatalogDto>;

public class GetPermissionCatalogQueryHandler(IApplicationDbContext db, CompanyAccess access)
    : IRequestHandler<GetPermissionCatalogQuery, PermissionCatalogDto>
{
    public async Task<PermissionCatalogDto> Handle(GetPermissionCatalogQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var enabled = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => t.EnabledModules).FirstOrDefaultAsync(ct) ?? [];

        var groups = PermissionCatalog.All
            .Where(p => p.ModuleKey is null || enabled.Contains(p.ModuleKey))
            .GroupBy(p => (p.Group, p.ModuleKey))
            .Select(g => new PermissionGroupDto(g.Key.Group, g.Key.ModuleKey,
                g.Select(p => new PermissionDto(p.Code, p.Name, p.Description)).ToList()))
            .ToList();
        return new PermissionCatalogDto(groups);
    }
}
