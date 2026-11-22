using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;

namespace IdentityService.Application.Account.Queries;

public sealed record SessionListItem(string Id, DateTimeOffset CreatedAt, string? Ip, string? UserAgent, bool IsCurrent, bool RememberMe);

/// <summary>Kullanıcının açık oturumları (en yeni önce).</summary>
[Authorize]
public record GetSessionsQuery : IRequest<IReadOnlyList<SessionListItem>>;

public class GetSessionsQueryHandler(ISessionStore sessions, ICurrentSession current)
    : IRequestHandler<GetSessionsQuery, IReadOnlyList<SessionListItem>>
{
    public async Task<IReadOnlyList<SessionListItem>> Handle(GetSessionsQuery request, CancellationToken ct)
    {
        var list = await sessions.ListForUserAsync(current.UserId!.Value, ct);
        return list.Select(s => new SessionListItem(
                s.Record.Id,
                DateTimeOffset.FromUnixTimeSeconds(s.Record.CreatedAt),
                s.Record.Ip,
                s.Record.UserAgent,
                s.SidHash == current.SidHash,
                s.Record.RememberMe))
            .ToList();
    }
}
