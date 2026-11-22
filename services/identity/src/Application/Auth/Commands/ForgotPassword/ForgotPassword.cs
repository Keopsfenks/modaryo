using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.ForgotPassword;

/// <summary>Şifre sıfırlama e-postası gönderir. Yanıt hesabın varlığını belli etmez.</summary>
public record ForgotPasswordCommand : IRequest
{
    public string Email { get; init; } = string.Empty;
}

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(c => c.Email).ValidEmail();
    }
}

public class ForgotPasswordCommandHandler(IUserAccounts users, IEmailComposer composer, IEmailSender sender, IAttemptLimiter limiter)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var email = IUserAccounts.Normalize(request.Email);

        if (!await limiter.TryAcquireAsync($"forgot:{email}", 3, TimeSpan.FromHours(1), ct))
        {
            throw new AuthException(StatusCodes.Status429TooManyRequests, AuthErrorCodes.RateLimited,
                "Çok fazla istek yaptınız. Lütfen daha sonra tekrar deneyin.");
        }

        var user = await users.FindByEmailAsync(email, ct);
        if (user is null)
        {
            return;
        }

        var token = await users.CreatePasswordResetTokenAsync(user.Id);
        await sender.SendAsync(composer.ResetPassword(user.Email, user.FullName, token), ct);
    }
}
