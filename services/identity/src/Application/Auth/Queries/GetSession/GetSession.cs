using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;

namespace IdentityService.Application.Auth.Queries.GetSession;

/// <summary>Oturumdaki kullanıcı, aktif firma, izinler ve üye olunan firmalar (güncel adlarla, veritabanından).</summary>
[Authorize]
public record GetSessionQuery : IRequest<SessionInfo>;

public class GetSessionQueryHandler(ICurrentSession current, ActiveTenantResolver resolver) : IRequestHandler<GetSessionQuery, SessionInfo>
{
    public Task<SessionInfo> Handle(GetSessionQuery request, CancellationToken ct) =>
        resolver.BuildSessionInfoAsync(current.UserId!.Value, current.TenantId, ct);
}
