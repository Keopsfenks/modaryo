namespace ERP.Domain.Inventory;

public class Lot : BaseEntity, ITenantOwned {
    public Guid           ProductId  { get; set; }
    public Guid?          VariantId  { get; set; }
    public string         LotNo      { get; set; } = string.Empty;
    public DateOnly?      ExpiryDate { get; set; }
    public DateTimeOffset CreatedAt  { get; set; }
    public Guid           TenantId   { get; set; }
}
