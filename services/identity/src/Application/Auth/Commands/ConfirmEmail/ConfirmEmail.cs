using IdentityService.Application.Common.Exceptions;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Application.Auth.Commands.ConfirmEmail;

/// <summary>E-postayı doğrular ve oturum açar (link e-posta sahipliğini kanıtlar).</summary>
public record ConfirmEmailCommand : IRequest<IssuedSession>
{
    public Guid UserId { get; init; }

    public string Token { get; init; } = string.Empty;
}

public class ConfirmEmailCommandHandler(IUserAccounts users, SessionIssuer issuer)
    : IRequestHandler<ConfirmEmailCommand, IssuedSession>
{
    public async Task<IssuedSession> Handle(ConfirmEmailCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || !await users.ConfirmEmailAsync(request.UserId, request.Token))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, AuthErrorCodes.InvalidToken,
                "Doğrulama bağlantısı geçersiz veya süresi dolmuş. Yeni bir bağlantı isteyebilirsiniz.");
        }

        var user = await users.FindByIdAsync(request.UserId, ct) ?? throw new UnauthorizedAccessException();
        return await issuer.IssueAsync(user, rememberMe: false, preferredTenantId: null, ct);
    }
}
