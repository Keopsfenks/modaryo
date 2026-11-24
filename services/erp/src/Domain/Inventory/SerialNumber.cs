namespace ERP.Domain.Inventory;

public class SerialNumber : BaseEntity, ITenantOwned {
    public Guid   ProductId   { get; set; }
    public Guid?  VariantId   { get; set; }
    public string Serial      { get; set; } = string.Empty;

    /// <summary>null = stokta değil.</summary>
    public Guid?  WarehouseId { get; set; }
    public Guid   TenantId    { get; set; }
}
