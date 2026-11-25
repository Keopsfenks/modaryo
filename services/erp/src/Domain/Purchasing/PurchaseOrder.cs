namespace ERP.Domain.Purchasing;

public class PurchaseOrder : BaseAuditableEntity, ITenantOwned {
    public string              Number       { get; set; } = string.Empty;
    public PurchaseOrderStatus Status       { get; set; } = PurchaseOrderStatus.Draft;
    public Guid                SupplierId   { get; set; }
    public Guid                BranchId     { get; set; }
    public DateOnly            Date         { get; set; }
    public DateOnly?           ExpectedDate { get; set; }
    public string              Currency     { get; set; } = "TRY";

    /// <summary>Yalnızca bilgi amaçlı; geçerli kur mal kabul anında alınır.</summary>
    public decimal Rate         { get; set; } = 1m;
    public string? Notes        { get; set; }
    public string? CancelReason { get; set; }
    public string? CloseReason  { get; set; }

    // Toplamlar (belge para birimi)
    public decimal Gross    { get; set; }
    public decimal Discount { get; set; }
    public decimal Net      { get; set; }
    public decimal Vat      { get; set; }
    public decimal Total    { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = new();

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint Version { get; set; }

    public Guid TenantId { get; set; }

    public bool IsEditable => Status == PurchaseOrderStatus.Draft;
}
