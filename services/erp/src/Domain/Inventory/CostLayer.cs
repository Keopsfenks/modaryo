namespace ERP.Domain.Inventory;

/// <summary>FIFO katmanı: depo x kalem başına giriş hareketi başına bir katman.</summary>
public class CostLayer : BaseEntity, ITenantOwned {
    public Guid     WarehouseId       { get; set; }
    public Guid     ProductId         { get; set; }
    public Guid?    VariantId         { get; set; }
    public Guid     InboundMovementId { get; set; }
    public DateOnly Date              { get; set; }
    public long     Sequence          { get; set; }
    public decimal  OriginalQty       { get; set; }
    public decimal  RemainingQty      { get; set; }
    public decimal  UnitCost          { get; set; }
    public Guid     TenantId          { get; set; }
}
