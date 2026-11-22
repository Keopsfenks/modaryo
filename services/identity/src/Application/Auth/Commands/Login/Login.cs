using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.Login;

public record LoginCommand : IRequest<IssuedSession>
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().WithMessage("E-posta adresi gereklidir.");
        RuleFor(c => c.Password).NotEmpty().WithMessage("Şifre gereklidir.");
    }
}

/// <summary>
/// Yanlış e-posta ve yanlış şifre aynı yanıtı verir. Kilit (5 hatalı deneme → 15 dk) ve doğrulanmamış e-posta
/// ayrıca bildirilir; bunlar yalnızca hedef alınmış hesaplarda varlığı belli eder (spec 7.5).
/// </summary>
public class LoginCommandHandler(IUserAccounts users, SessionIssuer issuer, IAttemptLimiter limiter, TimeProvider time)
    : IRequestHandler<LoginCommand, IssuedSession>
{
    public async Task<IssuedSession> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = IUserAccounts.Normalize(request.Email);

        if (!await limiter.TryAcquireAsync($"login:{email}", 5, TimeSpan.FromMinutes(1), ct))
        {
            throw new AuthException(StatusCodes.Status429TooManyRequests, AuthErrorCodes.RateLimited,
                "Çok fazla giriş denemesi yaptınız. Lütfen bir dakika sonra tekrar deneyin.");
        }

        var check = await users.CheckPasswordAsync(email, request.Password);

        return check.Result switch
        {
            PasswordCheckResult.Success => await issuer.IssueAsync(check.User!, request.RememberMe, preferredTenantId: null, ct),

            PasswordCheckResult.NotConfirmed => throw new AuthException(StatusCodes.Status403Forbidden, AuthErrorCodes.EmailNotConfirmed,
                "E-posta adresinizi henüz doğrulamadınız. Gelen kutunuzu kontrol edin veya yeni bağlantı isteyin."),

            PasswordCheckResult.LockedOut => throw new AuthException(StatusCodes.Status423Locked, AuthErrorCodes.AccountLocked,
                $"Çok fazla hatalı deneme yapıldı. Hesabınız {MinutesLeft(check.LockoutEnd)} dakika kilitli."),

            _ => throw new AuthException(StatusCodes.Status401Unauthorized, AuthErrorCodes.InvalidCredentials,
                "E-posta veya şifre hatalı."),
        };
    }

    private int MinutesLeft(DateTimeOffset? lockoutEnd) =>
        lockoutEnd is null ? 15 : Math.Max(1, (int)Math.Ceiling((lockoutEnd.Value - time.GetUtcNow()).TotalMinutes));
}
