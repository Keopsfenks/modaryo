using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Companies;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Invitations;

public sealed record InvitationDto(Guid Id, string Email, Guid RoleId, string RoleName, DateTimeOffset ExpiresAt, string InvitedBy);

public sealed record InvitationPreview(string CompanyName, string InvitedBy, string Email, DateTimeOffset ExpiresAt, bool AccountExists);

public sealed record MyInvitationDto(Guid Id, string CompanyName, string InvitedBy, string RoleName, DateTimeOffset ExpiresAt);

/// <summary>
/// Davet token'ları 32 rastgele bayt (base64url); yalnızca SHA-256 özeti saklanır. Ömür 7 gün.
/// </summary>
public sealed class InvitationService(
    IApplicationDbContext db,
    IUserAccounts users,
    IEmailComposer composer,
    IEmailSender sender,
    MembershipChanges changes,
    TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<Invitation?> FindPendingByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = SessionKeys.HashSid(token);
        var invitation = await db.Invitations.Include(i => i.Tenant).Include(i => i.Role)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invitation is not null && invitation.IsPending(time.GetUtcNow()) ? invitation : null;
    }

    /// <summary>Yeni token üretir (eskisi geçersizleşir) ve süreyi baştan başlatır. Token yalnızca e-postaya yazılır.</summary>
    public string Renew(Invitation invitation)
    {
        var token = SessionKeys.NewSid();
        var now = time.GetUtcNow();
        invitation.TokenHash = SessionKeys.HashSid(token);
        invitation.CreatedAt = now;
        invitation.ExpiresAt = now.Add(Lifetime);
        return token;
    }

    public async Task SendAsync(Invitation invitation, string token, CancellationToken ct)
    {
        var companyName = invitation.Tenant?.Name
            ?? await db.Tenants.Where(t => t.Id == invitation.TenantId).Select(t => t.Name).SingleAsync(ct);
        var inviter = await InviterNameAsync(invitation.InvitedByUserId, ct);
        await sender.SendAsync(composer.Invitation(invitation.Email, companyName, inviter, token), ct);
    }

    /// <summary>Üyelik oluşturur (yoksa), daveti kabul edildi işaretler ve verilen oturumu bu firmaya geçirir.</summary>
    public async Task AcceptAsync(Invitation invitation, Guid userId, string? currentSidHash, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (!await db.Memberships.AnyAsync(m => m.TenantId == invitation.TenantId && m.UserId == userId, ct))
        {
            db.Memberships.Add(new Membership { TenantId = invitation.TenantId, UserId = userId, RoleId = invitation.RoleId, JoinedAt = now });
        }

        invitation.AcceptedAt = now;
        await db.SaveChangesAsync(ct);
        await changes.OnJoinedAsync(userId, invitation.TenantId, currentSidHash, ct);
    }

    public async Task<string> InviterNameAsync(Guid inviterId, CancellationToken ct) =>
        (await users.FindByIdAsync(inviterId, ct))?.FullName ?? "—";

    public async Task<IReadOnlyDictionary<Guid, string>> InviterNamesAsync(IEnumerable<Guid> inviterIds, CancellationToken ct) =>
        (await users.FindManyAsync(inviterIds, ct)).ToDictionary(kv => kv.Key, kv => kv.Value.FullName);
}
