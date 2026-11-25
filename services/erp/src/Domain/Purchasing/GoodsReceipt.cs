namespace ERP.Domain.Purchasing;

public class GoodsReceipt : BaseAuditableEntity, ITenantOwned {
    public string             Number             { get; set; } = string.Empty;
    public GoodsReceiptStatus Status             { get; set; } = GoodsReceiptStatus.Posted;
    public Guid?              PurchaseOrderId    { get; set; }
    public Guid               SupplierId         { get; set; }
    public Guid               BranchId           { get; set; }
    public Guid               WarehouseId        { get; set; }
    public DateOnly           Date               { get; set; }
    public string             Currency           { get; set; } = "TRY";
    public decimal            Rate               { get; set; } = 1m;
    public string?            SupplierDeliveryNo { get; set; }
    public string?            Notes              { get; set; }
    public string?            CancelReason       { get; set; }

    public List<GoodsReceiptLine> Lines { get; set; } = new();

    public Guid TenantId { get; set; }
}
