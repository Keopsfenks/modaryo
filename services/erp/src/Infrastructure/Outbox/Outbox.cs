using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Platform;
using ERP.Infrastructure.Data;

namespace ERP.Infrastructure.Outbox;

public sealed class Outbox(
    ApplicationDbContext                     db,
    ICurrentTenant                           tenant,
    TimeProvider                             time,
    IEnumerable<OutboxSubscriberDescriptor>  subscribers
) : IOutbox {
    public static readonly JsonSerializerOptions DataJson = new() {
                                                                      PropertyNamingPolicy
                                                                          = JsonNamingPolicy.SnakeCaseLower
                                                                  };

    public void Enqueue(string type, object data) {
        var tenantId = tenant.TenantId
                       ?? throw new InvalidOperationException("Aktif firma olmadan olay kuyruğa alınamaz.");

        var message = new OutboxMessage {
                                            TenantId    = tenantId,
                                            Type        = type,
                                            PayloadJson = JsonSerializer.Serialize(data, DataJson),
                                            Actor       = tenant.UserFullName ?? "sistem",
                                            OccurredAt  = time.GetUtcNow()
                                        };
        db.OutboxMessages.Add(message);

        // Abone satırları iş değişikliğiyle aynı işlemde açılır: abonelerin işlenmesi akış (Valkey) iletimine bağlı değildir.
        foreach (var subscriber in subscribers.Where(s => s.Handles(type)))
            db.OutboxSubscriberStates.Add(new OutboxSubscriberState {
                                                                        MessageId = message.Id, Subscriber = subscriber.Name, TenantId = tenantId, NextAttemptAt = message.OccurredAt
                                                                    });
    }
}
