using IdentityService.Application.Auth;
using IdentityService.Application.Common.Interfaces;

namespace IdentityService.Application.Companies;

/// <summary>
/// Üyelik değiştiğinde oturumları ve temizlik sayacını güncelleyen tek yer (spec 6.7, 7.6).
/// Çağırmadan önce üyelik değişikliği kaydedilmiş olmalıdır.
/// </summary>
public sealed class MembershipChanges(
    ISessionStore sessions,
    ActiveTenantResolver resolver,
    IUserAccounts users,
    IApplicationDbContext db,
    TimeProvider time)
{
    /// <summary>Katılınan firma, verilen oturumun aktif firması olur; temizlik sayacı durur.</summary>
    public async Task OnJoinedAsync(Guid userId, Guid tenantId, string? currentSidHash, CancellationToken ct)
    {
        var permissions = await resolver.PermissionsForAsync(userId, tenantId, ct) ?? [];

        if (currentSidHash is not null)
        {
            await sessions.UpdateTenantAsync(currentSidHash, tenantId, permissions, ct);
        }

        await users.SetLastActiveTenantAsync(userId, tenantId);
        await users.SetLastMembershipEndedAsync(userId, null);
    }

    /// <summary>Kullanıcının o firmadaki tüm açık oturumlarının izinleri yenilenir.</summary>
    public async Task OnRoleChangedAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var permissions = await resolver.PermissionsForAsync(userId, tenantId, ct) ?? [];
        await sessions.UpdateTenantForUserAsync(userId, tenantId, tenantId, permissions, ct);
    }

    /// <summary>O firmadaki oturumlar firmasız kalır; başka üyelik yoksa temizlik sayacı başlar.</summary>
    public async Task OnLeftAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        await sessions.UpdateTenantForUserAsync(userId, tenantId, null, [], ct);

        var user = await users.FindByIdAsync(userId, ct);
        if (user?.LastActiveTenantId == tenantId)
        {
            await users.SetLastActiveTenantAsync(userId, null);
        }

        if (!await db.Memberships.AnyAsync(m => m.UserId == userId, ct))
        {
            await users.SetLastMembershipEndedAsync(userId, time.GetUtcNow());
        }
    }
}
