using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;

namespace ERP.Infrastructure.Outbox;

public sealed class Outbox(
    ApplicationDbContext db,
    ICurrentTenant       tenant,
    TimeProvider         time
) : IOutbox {
    public static readonly JsonSerializerOptions DataJson = new() {
                                                                      PropertyNamingPolicy
                                                                          = JsonNamingPolicy.SnakeCaseLower
                                                                  };

    public void Enqueue(string type, object data) {
        var tenantId = tenant.TenantId
                       ?? throw new InvalidOperationException("Aktif firma olmadan olay kuyruğa alınamaz.");

        db.OutboxMessages.Add(new OutboxMessage {
                                                    TenantId    = tenantId,
                                                    Type        = type,
                                                    PayloadJson = JsonSerializer.Serialize(data, DataJson),
                                                    Actor       = tenant.UserFullName ?? "sistem",
                                                    OccurredAt  = time.GetUtcNow()
                                                });
    }
}
