using IdentityService.Application.Auth;
using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Invitations.Commands;

/// <summary>
/// Davet gönderir. Aynı e-postaya bekleyen davet varsa o güncellenir (yeni token, yeni rol, yeni süre).
/// Sahip rolüyle davet yalnızca sahip gönderebilir.
/// </summary>
[Authorize(Permission = Permissions.MembersInvite)]
public record CreateInvitationCommand : IRequest<InvitationDto>
{
    public string Email { get; init; } = string.Empty;

    public Guid RoleId { get; init; }
}

public class CreateInvitationCommandValidator : AbstractValidator<CreateInvitationCommand>
{
    public CreateInvitationCommandValidator()
    {
        RuleFor(c => c.Email).ValidEmail();
        RuleFor(c => c.RoleId).NotEmpty().WithMessage("Rol seçilmelidir.");
    }
}

public class CreateInvitationCommandHandler(
    IApplicationDbContext db, CompanyAccess access, IUserAccounts users, InvitationService invitations)
    : IRequestHandler<CreateInvitationCommand, InvitationDto>
{
    public async Task<InvitationDto> Handle(CreateInvitationCommand request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var role = await access.RoleAsync(request.RoleId, ct);
        if (role.SystemKey == SystemRoles.Owner)
        {
            await access.EnsureCallerIsOwnerAsync(ct);
        }

        var email = IUserAccounts.Normalize(request.Email);
        var existingUser = await users.FindByEmailAsync(email, ct);
        if (existingUser is not null && await db.Memberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == existingUser.Id, ct))
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.AlreadyMember, "Bu kişi zaten firmanın üyesi.");
        }

        var invitation = await db.Invitations.FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (invitation is null)
        {
            invitation = new Invitation { TenantId = tenantId, Email = email };
            db.Invitations.Add(invitation);
        }

        invitation.RoleId = role.Id;
        invitation.InvitedByUserId = access.UserId;
        var token = invitations.Renew(invitation);
        await db.SaveChangesAsync(ct);

        await invitations.SendAsync(invitation, token, ct);
        return new InvitationDto(invitation.Id, invitation.Email, role.Id, role.Name, invitation.ExpiresAt,
            await invitations.InviterNameAsync(invitation.InvitedByUserId, ct));
    }
}

[Authorize(Permission = Permissions.MembersInvite)]
public record ResendInvitationCommand(Guid Id) : IRequest;

public class ResendInvitationCommandHandler(IApplicationDbContext db, CompanyAccess access, InvitationService invitations)
    : IRequestHandler<ResendInvitationCommand>
{
    public async Task Handle(ResendInvitationCommand request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var invitation = await db.Invitations.Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null, ct)
            ?? throw new NotFoundException(request.Id.ToString(), "Invitation");

        var token = invitations.Renew(invitation);
        await db.SaveChangesAsync(ct);
        await invitations.SendAsync(invitation, token, ct);
    }
}

[Authorize(Permission = Permissions.MembersInvite)]
public record RevokeInvitationCommand(Guid Id) : IRequest;

public class RevokeInvitationCommandHandler(IApplicationDbContext db, CompanyAccess access, TimeProvider time)
    : IRequestHandler<RevokeInvitationCommand>
{
    public async Task Handle(RevokeInvitationCommand request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == request.Id && i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null, ct)
            ?? throw new NotFoundException(request.Id.ToString(), "Invitation");

        invitation.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Davet linkiyle kabul: oturumdaki e-posta davet edilen e-postayla aynı olmalı.</summary>
[Authorize]
public record AcceptInvitationCommand(string Token) : IRequest<SessionInfo>;

public class AcceptInvitationCommandHandler(InvitationService invitations, IUserAccounts users, ICurrentSession current, ActiveTenantResolver resolver)
    : IRequestHandler<AcceptInvitationCommand, SessionInfo>
{
    public async Task<SessionInfo> Handle(AcceptInvitationCommand request, CancellationToken ct)
    {
        var invitation = await invitations.FindPendingByTokenAsync(request.Token, ct)
            ?? throw new NotFoundException("token", "Invitation");

        var userId = current.UserId!.Value;
        var user = await users.FindByIdAsync(userId, ct) ?? throw new UnauthorizedAccessException();
        if (IUserAccounts.Normalize(user.Email) != invitation.Email)
        {
            throw new AuthException(StatusCodes.Status403Forbidden, AuthErrorCodes.InvitationEmailMismatch,
                $"Bu davet {MaskEmail(invitation.Email)} adresine gönderildi. Lütfen o hesapla giriş yapın.");
        }

        await invitations.AcceptAsync(invitation, userId, current.SidHash, ct);
        return await resolver.BuildSessionInfoAsync(userId, invitation.TenantId, ct);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}***{email[at..]}";
    }
}

/// <summary>Başlangıç ekranından, oturumdaki e-postaya gelmiş bir daveti kimliğiyle kabul eder.</summary>
[Authorize]
public record AcceptMyInvitationCommand(Guid Id) : IRequest<SessionInfo>;

public class AcceptMyInvitationCommandHandler(
    IApplicationDbContext db, InvitationService invitations, IUserAccounts users, ICurrentSession current, ActiveTenantResolver resolver, TimeProvider time)
    : IRequestHandler<AcceptMyInvitationCommand, SessionInfo>
{
    public async Task<SessionInfo> Handle(AcceptMyInvitationCommand request, CancellationToken ct)
    {
        var userId = current.UserId!.Value;
        var email = IUserAccounts.Normalize((await users.FindByIdAsync(userId, ct) ?? throw new UnauthorizedAccessException()).Email);

        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == request.Id && i.Email == email, ct);
        if (invitation is null || !invitation.IsPending(time.GetUtcNow()))
        {
            throw new NotFoundException(request.Id.ToString(), "Invitation");
        }

        await invitations.AcceptAsync(invitation, userId, current.SidHash, ct);
        return await resolver.BuildSessionInfoAsync(userId, invitation.TenantId, ct);
    }
}
