using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Auth;

/// <summary>Kullanıcının aktif firmasını ve o firmadaki izinlerini belirler.</summary>
public sealed class ActiveTenantResolver(IApplicationDbContext db, IUserAccounts users)
{
    /// <summary>
    /// Öncelik: <paramref name="preferredTenantId"/> (hâlâ üyeyse) → LastActiveTenantId → en son katılınan firma.
    /// Hiç üyelik yoksa <see cref="SessionGrant.None"/>.
    /// </summary>
    public async Task<SessionGrant> ResolveAsync(Guid userId, Guid? preferredTenantId, CancellationToken ct)
    {
        var memberships = await MembershipsAsync(userId, ct);
        if (memberships.Count == 0)
        {
            return SessionGrant.None;
        }

        var lastActive = (await users.FindByIdAsync(userId, ct))?.LastActiveTenantId;
        var chosen = memberships.FirstOrDefault(m => m.TenantId == preferredTenantId)
            ?? memberships.FirstOrDefault(m => m.TenantId == lastActive)
            ?? memberships[0];

        return GrantOf(chosen);
    }

    /// <summary>Üye değilse null.</summary>
    public async Task<SessionGrant?> GrantForAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var membership = await db.Memberships.AsNoTracking()
            .Include(m => m.Tenant)
            .Include(m => m.Role).ThenInclude(r => r.Permissions)
            .Include(m => m.Role).ThenInclude(r => r.Branches)
            .Include(m => m.Branches)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        return membership is null ? null : GrantOf(membership);
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
        var grant = active is null ? SessionGrant.None : GrantOf(active);

        return new SessionInfo(
            new UserDto(user.Id, user.Email, user.FullName),
            active is null ? null : new CompanyRef(active.TenantId, active.Tenant.Name, active.Role.Name),
            active is null ? [] : PermissionsOf(active),
            companies,
            grant.Modules,
            grant.BranchIds,
            grant.MaxDiscountPct,
            grant.UserType);
    }

    private Task<List<Membership>> MembershipsAsync(Guid userId, CancellationToken ct) =>
        db.Memberships.AsNoTracking()
            .Include(m => m.Tenant)
            .Include(m => m.Role).ThenInclude(r => r.Permissions)
            .Include(m => m.Role).ThenInclude(r => r.Branches)
            .Include(m => m.Branches)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync(ct);

    private static SessionGrant GrantOf(Membership membership) => new(
        membership.TenantId,
        PermissionsOf(membership),
        membership.Tenant.EnabledModules.Order(StringComparer.Ordinal).ToArray(),
        BranchScopeOf(membership),
        membership.Role.MaxDiscountPercent,
        membership.Kind,
        membership.CariId);

    /// <summary>Rol kapsamı (AllBranches ise null) ile üyelik daraltmasının kesişimi; boş kesişim boş dizidir.</summary>
    private static Guid[]? BranchScopeOf(Membership membership)
    {
        var role = membership.Role.AllBranches ? null : membership.Role.Branches.Select(b => b.BranchId).ToHashSet();
        var narrowing = membership.Branches.Count == 0 ? null : membership.Branches.Select(b => b.BranchId).ToHashSet();

        return (role, narrowing) switch
        {
            (null, null) => null,
            (not null, null) => role.ToArray(),
            (null, not null) => narrowing.ToArray(),
            _ => role!.Intersect(narrowing!).ToArray(),
        };
    }

    private static string[] PermissionsOf(Membership membership) =>
        membership.Role.Permissions.Select(p => p.Permission).OrderBy(p => p, StringComparer.Ordinal).ToArray();
}
