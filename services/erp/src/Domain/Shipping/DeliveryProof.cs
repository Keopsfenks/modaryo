namespace ERP.Domain.Shipping;

/// <summary>Teslim kanıtı: teslim alan, zaman, isteğe bağlı fotoğraf/imza (dosya türü "image") ve not.</summary>
public class DeliveryProof : BaseAuditableEntity, ITenantOwned {
    public Guid           ShipmentId  { get; set; }
    public string         ReceivedBy  { get; set; } = string.Empty;
    public DateTimeOffset DeliveredAt { get; set; }
    public Guid?          PhotoFileId { get; set; }
    public string?        Note        { get; set; }
    public Guid           TenantId    { get; set; }
}
