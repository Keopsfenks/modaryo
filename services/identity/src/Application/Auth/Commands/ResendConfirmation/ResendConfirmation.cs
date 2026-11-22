using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.ResendConfirmation;

/// <summary>Doğrulama e-postasını yeniden gönderir. Yanıt hesabın varlığını belli etmez.</summary>
public record ResendConfirmationCommand : IRequest
{
    public string Email { get; init; } = string.Empty;
}

public class ResendConfirmationCommandValidator : AbstractValidator<ResendConfirmationCommand>
{
    public ResendConfirmationCommandValidator()
    {
        RuleFor(c => c.Email).ValidEmail();
    }
}

public class ResendConfirmationCommandHandler(IUserAccounts users, IEmailComposer composer, IEmailSender sender, IAttemptLimiter limiter)
    : IRequestHandler<ResendConfirmationCommand>
{
    public async Task Handle(ResendConfirmationCommand request, CancellationToken ct)
    {
        var email = IUserAccounts.Normalize(request.Email);

        if (!await limiter.TryAcquireAsync($"resend:{email}", 3, TimeSpan.FromHours(1), ct))
        {
            throw new AuthException(StatusCodes.Status429TooManyRequests, AuthErrorCodes.RateLimited,
                "Çok fazla istek yaptınız. Lütfen daha sonra tekrar deneyin.");
        }

        var user = await users.FindByEmailAsync(email, ct);
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
        await sender.SendAsync(composer.ConfirmEmail(user.Email, user.FullName, user.Id, token), ct);
    }
}
