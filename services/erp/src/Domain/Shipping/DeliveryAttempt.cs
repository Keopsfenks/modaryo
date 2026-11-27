namespace ERP.Domain.Shipping;

public enum DeliveryOutcome { Pending, Delivered, Failed }

/// <summary>Teslimat denemesi. İlk sevk 1. deneme; başarısız sonrası yeniden gönderim yeni deneme açar.</summary>
public class DeliveryAttempt : BaseEntity, ITenantOwned {
    public Guid            ShipmentId    { get; set; }
    public int             AttemptNo     { get; set; }
    public DateTimeOffset  SentAt        { get; set; }
    public DeliveryOutcome Outcome       { get; set; } = DeliveryOutcome.Pending;
    public DateTimeOffset? ResolvedAt    { get; set; }
    public string?         FailureReason { get; set; }
    public Guid            TenantId      { get; set; }
}
