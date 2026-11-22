using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;

namespace IdentityService.Application.Auth.Commands.Logout;

/// <summary>Bu oturumu kapatır. Oturum zaten yoksa da başarılı sayılır (cookie yine silinir).</summary>
public record LogoutCommand : IRequest;

public class LogoutCommandHandler(ISessionStore store, ICurrentSession current) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (current.SidHash is { } sidHash)
        {
            await store.DeleteAsync(sidHash, ct);
        }
    }
}

/// <summary>Kullanıcının tüm cihazlardaki oturumlarını kapatır.</summary>
[Authorize]
public record LogoutAllCommand : IRequest;

public class LogoutAllCommandHandler(ISessionStore store, ICurrentSession current) : IRequestHandler<LogoutAllCommand>
{
    public async Task Handle(LogoutAllCommand request, CancellationToken ct)
    {
        await store.DeleteAllForUserAsync(current.UserId!.Value, exceptSidHash: null, ct);
    }
}
