namespace ERP.Domain.Inventory;

public class MinStockLevel : BaseEntity, ITenantOwned {
    public Guid     WarehouseId { get; set; }
    public Guid     ProductId   { get; set; }
    public Guid?    VariantId   { get; set; }
    public decimal  MinQty      { get; set; }
    public decimal? ReorderQty  { get; set; }
    public Guid     TenantId    { get; set; }
}
