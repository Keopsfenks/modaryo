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
        if (currentSidHash is not null)
        {
            var grant = await resolver.GrantForAsync(userId, tenantId, ct) ?? SessionGrant.None;
            await sessions.UpdateGrantAsync(currentSidHash, grant, ct);
        }

        await users.SetLastActiveTenantAsync(userId, tenantId);
        await users.SetLastMembershipEndedAsync(userId, null);
    }

    /// <summary>Kullanıcının o firmadaki tüm açık oturumlarının yetki paketi yenilenir.</summary>
    public async Task OnRoleChangedAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var grant = await resolver.GrantForAsync(userId, tenantId, ct) ?? SessionGrant.None;
        await sessions.UpdateGrantForUserAsync(userId, tenantId, grant, ct);
    }

    /// <summary>Firmanın her üyesinin o firmadaki açık oturumlarının yetki paketi yenilenir (modül, rol değişiklikleri).</summary>
    public async Task RefreshTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var userIds = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            await OnRoleChangedAsync(userId, tenantId, ct);
        }
    }

    /// <summary>Rol tanımı değişti: o rolü taşıyan her üyenin açık oturumlarının yetki paketi yenilenir.</summary>
    public async Task OnRoleDefinitionChangedAsync(Guid roleId, CancellationToken ct)
    {
        var members = await db.Memberships.AsNoTracking()
            .Where(m => m.RoleId == roleId)
            .Select(m => new { m.UserId, m.TenantId })
            .ToListAsync(ct);

        foreach (var member in members)
        {
            await OnRoleChangedAsync(member.UserId, member.TenantId, ct);
        }
    }

    /// <summary>O firmadaki oturumlar firmasız kalır; başka üyelik yoksa temizlik sayacı başlar.</summary>
    public async Task OnLeftAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        await sessions.UpdateGrantForUserAsync(userId, tenantId, SessionGrant.None, ct);

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
