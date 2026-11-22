using System.Text.Json;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Domain.Entities;

namespace IdentityService.Infrastructure.Audit;

public sealed class AuditLog(IApplicationDbContext db, ICurrentSession current, TimeProvider time) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, string entityId, object? before, object? after)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            TenantId = current.TenantId ?? throw new InvalidOperationException("Denetim kaydı için aktif firma gerekir."),
            ActorUserId = current.UserId,
            ActorLabel = current.FullName ?? string.Empty,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Before = before is null ? null : JsonSerializer.Serialize(before, Json),
            After = after is null ? null : JsonSerializer.Serialize(after, Json),
            At = time.GetUtcNow(),
        });
    }
}
