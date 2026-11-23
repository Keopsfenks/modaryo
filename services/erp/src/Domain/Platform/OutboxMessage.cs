namespace ERP.Domain.Platform;

/// <summary>
///     Transactional outbox kaydı: iş değişikliğiyle aynı işlemde yazılır, OutboxRelay "erp.events" akışına iletir.
///     Id UUIDv7'dir ve olay zarfının (envelope) "id" alanıdır. PayloadJson zarfın "data" kısmıdır.
/// </summary>
public class OutboxMessage : ITenantOwned {
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Type { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public Guid TenantId { get; set; }
}
