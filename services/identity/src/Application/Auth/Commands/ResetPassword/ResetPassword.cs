using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.ResetPassword;

/// <summary>
/// E-postadaki kodla şifreyi değiştirir ve kullanıcının TÜM oturumlarını kapatır.
/// Kod e-posta sahipliğini kanıtladığından doğrulanmamış hesap da doğrulanmış sayılır.
/// </summary>
public record ResetPasswordCommand : IRequest
{
    public string Email { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;
}

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.NewPassword).ValidNewPassword();
    }
}

public class ResetPasswordCommandHandler(IUserAccounts users, ISessionStore sessions) : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email, ct);

        if (user is null || !await users.ResetPasswordAsync(user.Email, request.Token, request.NewPassword))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, AuthErrorCodes.InvalidToken,
                "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş. Yeni bir bağlantı isteyebilirsiniz.");
        }

        await users.MarkEmailConfirmedAsync(user.Id);
        await sessions.DeleteAllForUserAsync(user.Id, exceptSidHash: null, ct);
    }
}
