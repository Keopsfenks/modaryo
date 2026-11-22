using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Auth;

/// <summary>Kullanıcının aktif firmasını ve o firmadaki izinlerini belirler.</summary>
public sealed class ActiveTenantResolver(IApplicationDbContext db, IUserAccounts users)
{
    /// <summary>
    /// Öncelik: <paramref name="preferredTenantId"/> (hâlâ üyeyse) → LastActiveTenantId → en son katılınan firma.
    /// Hiç üyelik yoksa (null, []).
    /// </summary>
    public async Task<(Guid? TenantId, string[] Permissions)> ResolveAsync(Guid userId, Guid? preferredTenantId, CancellationToken ct)
    {
        var memberships = await MembershipsAsync(userId, ct);
        if (memberships.Count == 0)
        {
            return (null, []);
        }

        var lastActive = (await users.FindByIdAsync(userId, ct))?.LastActiveTenantId;
        var chosen = memberships.FirstOrDefault(m => m.TenantId == preferredTenantId)
            ?? memberships.FirstOrDefault(m => m.TenantId == lastActive)
            ?? memberships[0];

        return (chosen.TenantId, PermissionsOf(chosen));
    }

    /// <summary>Üye değilse null.</summary>
    public async Task<string[]?> PermissionsForAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var membership = await db.Memberships.AsNoTracking()
            .Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        return membership is null ? null : PermissionsOf(membership);
    }

    public async Task<SessionInfo> BuildSessionInfoAsync(Guid userId, Guid? activeTenantId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct) ?? throw new UnauthorizedAccessException();
        var memberships = await MembershipsAsync(userId, ct);

        var companies = memberships
            .Select(m => new CompanyRef(m.TenantId, m.Tenant.Name, m.Role.Name))
            .OrderBy(c => c.Name, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
            .ToArray();
        var active = memberships.FirstOrDefault(m => m.TenantId == activeTenantId);

        return new SessionInfo(
            new UserDto(user.Id, user.Email, user.FullName),
            active is null ? null : new CompanyRef(active.TenantId, active.Tenant.Name, active.Role.Name),
            active is null ? [] : PermissionsOf(active),
            companies);
    }

    private Task<List<Membership>> MembershipsAsync(Guid userId, CancellationToken ct) =>
        db.Memberships.AsNoTracking()
            .Include(m => m.Tenant)
            .Include(m => m.Role)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync(ct);

    private static string[] PermissionsOf(Membership membership) =>
        membership.Role.Permissions.Select(p => p.Permission).OrderBy(p => p, StringComparer.Ordinal).ToArray();
}
