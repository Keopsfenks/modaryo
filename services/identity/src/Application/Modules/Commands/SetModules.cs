using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Application.Modules.Queries;
using IdentityService.Domain.Constants;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Modules.Commands;

/// <summary>Firmanın açık modüllerini verilen kümeyle değiştirir; üyelerin canlı oturumları hemen güncellenir.</summary>
[Authorize(Permission = Permissions.SettingsModules)]
public record SetModulesCommand : IRequest<IReadOnlyList<ModuleDto>>
{
    public string[] Enabled { get; init; } = [];
}

public class SetModulesCommandHandler(IApplicationDbContext db, CompanyAccess access, MembershipChanges changes)
    : IRequestHandler<SetModulesCommand, IReadOnlyList<ModuleDto>>
{
    public async Task<IReadOnlyList<ModuleDto>> Handle(SetModulesCommand request, CancellationToken ct)
    {
        var requested = (request.Enabled ?? []).Distinct(StringComparer.Ordinal).ToList();
        var unknown = requested.FirstOrDefault(key => !ModuleKeys.IsKnown(key));
        if (unknown is not null)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, AuthErrorCodes.UnknownModule,
                $"Bilinmeyen modül: {unknown}.");
        }

        var tenantId = access.TenantId;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException(tenantId.ToString(), "Company");

        tenant.EnabledModules = ModuleKeys.All.Select(m => m.Key).Where(requested.Contains).ToList();
        await db.SaveChangesAsync(ct);

        await changes.RefreshTenantAsync(tenantId, ct);
        return ModuleList.For(tenant.EnabledModules);
    }
}
