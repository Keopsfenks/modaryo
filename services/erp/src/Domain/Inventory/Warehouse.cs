namespace ERP.Domain.Inventory;

public class Warehouse : BaseEntity, ITenantOwned {
    public Guid          BranchId  { get; set; }
    public string        Code      { get; set; } = string.Empty;
    public string        Name      { get; set; } = string.Empty;
    public WarehouseKind Kind      { get; set; } = WarehouseKind.Normal;
    public bool          IsDefault { get; set; }
    public bool          IsActive  { get; set; } = true;
    public Guid          TenantId  { get; set; }
}

public sealed record BundleComponent(Guid ProductId, Guid? VariantId, decimal Quantity);
