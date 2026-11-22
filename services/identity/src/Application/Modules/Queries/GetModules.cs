using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.Modules.Queries;

public sealed record ModuleDto(string Key, string Name, string Description, int Wave, bool Enabled);

/// <summary>Modül kataloğu; aktif firmada hangilerinin açık olduğu işaretlenir. Her firma üyesi okuyabilir.</summary>
[Authorize]
public record GetModulesQuery : IRequest<IReadOnlyList<ModuleDto>>;

public class GetModulesQueryHandler(IApplicationDbContext db, CompanyAccess access)
    : IRequestHandler<GetModulesQuery, IReadOnlyList<ModuleDto>>
{
    public async Task<IReadOnlyList<ModuleDto>> Handle(GetModulesQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var enabled = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
            .Select(t => t.EnabledModules).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(tenantId.ToString(), "Company");
        return ModuleList.For(enabled);
    }
}

public static class ModuleList
{
    public static IReadOnlyList<ModuleDto> For(IReadOnlyCollection<string> enabled) =>
        ModuleKeys.All.Select(m => new ModuleDto(m.Key, m.Name, m.Description, m.Wave, enabled.Contains(m.Key))).ToList();
}
