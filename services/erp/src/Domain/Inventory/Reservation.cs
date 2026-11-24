namespace ERP.Domain.Inventory;

public class Reservation : BaseEntity, ITenantOwned {
    public Guid              WarehouseId  { get; set; }
    public Guid              ProductId    { get; set; }
    public Guid?             VariantId    { get; set; }
    public decimal           Quantity     { get; set; }
    public string            SourceType   { get; set; } = string.Empty;
    public Guid              SourceId     { get; set; }
    public Guid              SourceLineId { get; set; }
    public ReservationStatus Status       { get; set; } = ReservationStatus.Active;

    /// <summary>Siparişe göre tedarikte beklenen giriş belgesi.</summary>
    public Guid?             ExpectedReceiptSourceId { get; set; }
    public Guid              TenantId     { get; set; }
}
