namespace ERP.Domain.Inventory;

public class StockCount : BaseEntity, ITenantOwned {
    public Guid             WarehouseId { get; set; }
    public string           Number      { get; set; } = string.Empty;
    public StockCountStatus Status      { get; set; } = StockCountStatus.Open;
    public DateTimeOffset   SnapshotAt  { get; set; }
    public DateOnly         Date        { get; set; }
    public string?          Note        { get; set; }
    public DateTimeOffset   CreatedAt   { get; set; }
    public DateTimeOffset?  PostedAt    { get; set; }

    public List<StockCountLine> Lines { get; set; } = new();

    public Guid TenantId { get; set; }
}
