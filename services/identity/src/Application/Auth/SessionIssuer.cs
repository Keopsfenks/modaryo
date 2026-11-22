using IdentityService.Application.Common.Interfaces;

namespace IdentityService.Application.Auth;

/// <summary>
/// Oturum açar: aktif firmayı çözer, Valkey'e kaydı yazar, yanıt gövdesini hazırlar. Cookie'yi uç nokta yazar.
/// İstekte zaten bir oturum varsa (yeniden giriş) eskisi silinir.
/// </summary>
public sealed class SessionIssuer(ISessionStore store, ActiveTenantResolver resolver, ICurrentSession current)
{
    public async Task<IssuedSession> IssueAsync(UserSummary user, bool rememberMe, Guid? preferredTenantId, CancellationToken ct)
    {
        if (current.SidHash is { } previous)
        {
            await store.DeleteAsync(previous, ct);
        }

        var (tenantId, permissions) = await resolver.ResolveAsync(user.Id, preferredTenantId, ct);

        var created = await store.CreateAsync(
            new NewSession(user.Id, user.Email, user.FullName, tenantId, permissions, rememberMe, current.Ip, current.UserAgent), ct);

        var info = await resolver.BuildSessionInfoAsync(user.Id, tenantId, ct);
        return new IssuedSession(created, info);
    }
}
