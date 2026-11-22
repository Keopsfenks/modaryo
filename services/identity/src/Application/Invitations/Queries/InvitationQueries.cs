using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;

namespace IdentityService.Application.Invitations.Queries;

[Authorize(Permission = Permissions.MembersInvite)]
public record ListInvitationsQuery : IRequest<IReadOnlyList<InvitationDto>>;

public class ListInvitationsQueryHandler(IApplicationDbContext db, CompanyAccess access, InvitationService invitations, TimeProvider time)
    : IRequestHandler<ListInvitationsQuery, IReadOnlyList<InvitationDto>>
{
    public async Task<IReadOnlyList<InvitationDto>> Handle(ListInvitationsQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var now = time.GetUtcNow();
        var pending = await db.Invitations.AsNoTracking().Include(i => i.Role)
            .Where(i => i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        var inviters = await invitations.InviterNamesAsync(pending.Select(i => i.InvitedByUserId), ct);

        return pending
            .Select(i => new InvitationDto(i.Id, i.Email, i.RoleId, i.Role.Name, i.ExpiresAt, inviters.GetValueOrDefault(i.InvitedByUserId, "—")))
            .ToList();
    }
}

/// <summary>
/// Davet linki önizlemesi (oturumsuz). Token e-posta sahipliğini kanıtladığından AccountExists bilgisini
/// yalnızca token sahibi görür.
/// </summary>
public record GetInvitationPreviewQuery(string Token) : IRequest<InvitationPreview>;

public class GetInvitationPreviewQueryHandler(InvitationService invitations, IUserAccounts users)
    : IRequestHandler<GetInvitationPreviewQuery, InvitationPreview>
{
    public async Task<InvitationPreview> Handle(GetInvitationPreviewQuery request, CancellationToken ct)
    {
        var invitation = await invitations.FindPendingByTokenAsync(request.Token, ct)
            ?? throw new NotFoundException("token", "Invitation");

        return new InvitationPreview(
            invitation.Tenant.Name,
            await invitations.InviterNameAsync(invitation.InvitedByUserId, ct),
            invitation.Email,
            invitation.ExpiresAt,
            await users.FindByEmailAsync(invitation.Email, ct) is not null);
    }
}

/// <summary>Oturumdaki kullanıcının e-postasına gelmiş bekleyen davetler (başlangıç ekranı).</summary>
[Authorize]
public record GetMyInvitationsQuery : IRequest<IReadOnlyList<MyInvitationDto>>;

public class GetMyInvitationsQueryHandler(
    IApplicationDbContext db, IUserAccounts users, ICurrentSession current, InvitationService invitations, TimeProvider time)
    : IRequestHandler<GetMyInvitationsQuery, IReadOnlyList<MyInvitationDto>>
{
    public async Task<IReadOnlyList<MyInvitationDto>> Handle(GetMyInvitationsQuery request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId!.Value, ct) ?? throw new UnauthorizedAccessException();
        var email = IUserAccounts.Normalize(user.Email);
        var now = time.GetUtcNow();

        var pending = await db.Invitations.AsNoTracking().Include(i => i.Tenant).Include(i => i.Role)
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
        var inviters = await invitations.InviterNamesAsync(pending.Select(i => i.InvitedByUserId), ct);

        return pending
            .Select(i => new MyInvitationDto(i.Id, i.Tenant.Name, inviters.GetValueOrDefault(i.InvitedByUserId, "—"), i.Role.Name, i.ExpiresAt))
            .ToList();
    }
}
