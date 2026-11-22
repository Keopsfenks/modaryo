using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Companies;

/// <summary>
/// Aktif firmaya kısıtlı sorgular ve sahiplik kuralları. Başka firmaya ait kimlikler her zaman "bulunamadı" sayılır.
/// </summary>
public sealed class CompanyAccess(IApplicationDbContext db, ICurrentSession current)
{
    public Guid TenantId => current.TenantId ?? throw new NotFoundException("current", "Company");

    public Guid UserId => current.UserId ?? throw new UnauthorizedAccessException();

    public async Task<Membership> MembershipAsync(Guid userId, CancellationToken ct) =>
        await db.Memberships.Include(m => m.Role)
            .FirstOrDefaultAsync(m => m.TenantId == TenantId && m.UserId == userId, ct)
        ?? throw new NotFoundException(userId.ToString(), "Member");

    public async Task<TenantRole> RoleAsync(Guid roleId, CancellationToken ct) =>
        await db.TenantRoles.FirstOrDefaultAsync(r => r.TenantId == TenantId && r.Id == roleId, ct)
        ?? throw new NotFoundException(roleId.ToString(), "Role");

    public async Task<bool> CallerIsOwnerAsync(CancellationToken ct) =>
        await db.Memberships.AnyAsync(m => m.TenantId == TenantId && m.UserId == UserId && m.Role.SystemKey == SystemRoles.Owner, ct);

    /// <summary>Sahip rolüne dokunan her işlem (vermek, almak, sahibi çıkarmak) yalnızca sahibe açıktır.</summary>
    public async Task EnsureCallerIsOwnerAsync(CancellationToken ct)
    {
        if (!await CallerIsOwnerAsync(ct))
        {
            throw new ForbiddenAccessException();
        }
    }

    /// <summary>Hedef firmadaki son sahipse 409 last_owner.</summary>
    public async Task EnsureNotLastOwnerAsync(Membership target, CancellationToken ct)
    {
        if (target.Role.SystemKey != SystemRoles.Owner)
        {
            return;
        }

        var owners = await db.Memberships.CountAsync(m => m.TenantId == target.TenantId && m.Role.SystemKey == SystemRoles.Owner, ct);
        if (owners <= 1)
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.LastOwner,
                "Firmanın son sahibi bu işlemi yapamaz. Önce başka bir üyeyi sahip yapın.");
        }
    }
}
