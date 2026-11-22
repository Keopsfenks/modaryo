using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Application.Common.Security;
using IdentityService.Application.Companies;
using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;

namespace IdentityService.Application.Audit.Queries;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset At,
    string ActorLabel,
    string Action,
    string EntityType,
    string EntityId,
    JsonElement? Before,
    JsonElement? After);

public sealed record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, int Total);

/// <summary>Aktif firmanın denetim kayıtları, yeniden eskiye. pageSize varsayılan 50, en çok 200.</summary>
[Authorize(Permission = Permissions.AuditRead)]
public record ListAuditQuery : IRequest<AuditPageDto>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? EntityType { get; init; }
}

public class ListAuditQueryHandler(IApplicationDbContext db, CompanyAccess access) : IRequestHandler<ListAuditQuery, AuditPageDto>
{
    public async Task<AuditPageDto> Handle(ListAuditQuery request, CancellationToken ct)
    {
        var tenantId = access.TenantId;
        var page = Math.Max(request.Page ?? 1, 1);
        var pageSize = Math.Clamp(request.PageSize ?? ListAuditQuery.DefaultPageSize, 1, ListAuditQuery.MaxPageSize);

        var query = db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityType = request.EntityType.Trim();
            query = query.Where(a => a.EntityType == entityType);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new AuditPageDto(rows.Select(ToDto).ToList(), total);
    }

    private static AuditEntryDto ToDto(AuditEntry a) =>
        new(a.Id, a.At, a.ActorLabel, a.Action, a.EntityType, a.EntityId, Parse(a.Before), Parse(a.After));

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
