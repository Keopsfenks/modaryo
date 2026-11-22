using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Invitations;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.Register;

/// <summary>
/// Yeni hesap. Normal akışta yanıt e-postanın kayıtlı olup olmadığını belli etmez (null döner → 202).
/// Geçerli bir davet token'ı ve eşleşen e-postayla hesap doğrulanmış açılır, davet kabul edilir ve oturum açılır.
/// </summary>
public record RegisterCommand : IRequest<IssuedSession?>
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string? InviteToken { get; init; }
}

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(c => c.Email).ValidEmail();
        RuleFor(c => c.Password).ValidNewPassword();
        RuleFor(c => c.FullName).ValidFullName();
    }
}

public class RegisterCommandHandler(
    IUserAccounts users,
    IEmailComposer composer,
    IEmailSender sender,
    InvitationService invitations,
    SessionIssuer issuer)
    : IRequestHandler<RegisterCommand, IssuedSession?>
{
    public async Task<IssuedSession?> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = IUserAccounts.Normalize(request.Email);

        if (request.InviteToken is not null
            && await invitations.FindPendingByTokenAsync(request.InviteToken, ct) is { } invitation
            && invitation.Email == email)
        {
            return await RegisterViaInvitationAsync(request, email, invitation, ct);
        }

        if (await users.FindByEmailAsync(email, ct) is not null)
        {
            await sender.SendAsync(composer.AccountAlreadyExists(email), ct);
            return null;
        }

        var user = await users.CreateAsync(email, request.FullName, request.Password, emailConfirmed: false, ct);
        var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
        await sender.SendAsync(composer.ConfirmEmail(user.Email, user.FullName, user.Id, token), ct);
        return null;
    }

    /// <summary>Davet linki e-posta sahipliğini kanıtlar; token sahibine hesabın varlığını söylemek sızıntı değildir.</summary>
    private async Task<IssuedSession> RegisterViaInvitationAsync(RegisterCommand request, string email, Domain.Entities.Invitation invitation, CancellationToken ct)
    {
        var existing = await users.FindByEmailAsync(email, ct);
        UserSummary user;

        if (existing is null)
        {
            user = await users.CreateAsync(email, request.FullName, request.Password, emailConfirmed: true, ct);
        }
        else if (!existing.EmailConfirmed)
        {
            await users.SetPasswordAsync(existing.Id, request.Password);
            await users.UpdateProfileAsync(existing.Id, request.FullName);
            await users.MarkEmailConfirmedAsync(existing.Id);
            user = (await users.FindByIdAsync(existing.Id, ct))!;
        }
        else
        {
            throw new AuthException(StatusCodes.Status409Conflict, AuthErrorCodes.AccountExists,
                "Bu e-postayla bir hesap var. Giriş yapıp daveti kabul edin.");
        }

        await invitations.AcceptAsync(invitation, user.Id, currentSidHash: null, ct);
        return await issuer.IssueAsync(user, rememberMe: false, preferredTenantId: invitation.TenantId, ct);
    }
}
