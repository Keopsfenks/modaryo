using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;

namespace IdentityService.Application.Auth.Commands.SwitchTenant;

/// <summary>Oturumun aktif firmasını değiştirir (profil menüsündeki firma değiştirici).</summary>
[Authorize]
public record SwitchTenantCommand : IRequest<SessionInfo>
{
    public Guid TenantId { get; init; }
}

public class SwitchTenantCommandHandler(ISessionStore store, ICurrentSession current, ActiveTenantResolver resolver, IUserAccounts users)
    : IRequestHandler<SwitchTenantCommand, SessionInfo>
{
    public async Task<SessionInfo> Handle(SwitchTenantCommand request, CancellationToken ct)
    {
        var userId = current.UserId!.Value;
        var permissions = await resolver.PermissionsForAsync(userId, request.TenantId, ct)
            ?? throw new ForbiddenAccessException();

        if (!await store.UpdateTenantAsync(current.SidHash!, request.TenantId, permissions, ct))
        {
            throw new UnauthorizedAccessException();
        }

        await users.SetLastActiveTenantAsync(userId, request.TenantId);
        return await resolver.BuildSessionInfoAsync(userId, request.TenantId, ct);
    }
}
