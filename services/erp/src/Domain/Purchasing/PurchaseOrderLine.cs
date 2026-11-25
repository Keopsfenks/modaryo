namespace ERP.Domain.Purchasing;

public class PurchaseOrderLine : BaseEntity, ITenantOwned {
    public Guid      PurchaseOrderId  { get; set; }
    public int       Sort             { get; set; }
    public Guid      ProductId        { get; set; }
    public Guid?     VariantId        { get; set; }
    public string    Description      { get; set; } = string.Empty;
    public string    Unit             { get; set; } = string.Empty;
    public decimal   Factor           { get; set; } = 1m;
    public decimal   Quantity         { get; set; }
    public decimal?  Width            { get; set; }
    public decimal?  Length           { get; set; }
    public decimal   UnitPrice        { get; set; }
    public decimal?  DiscountPercent  { get; set; }
    public int       VatRate          { get; set; }
    public DateOnly? ExpectedDate     { get; set; }
    public Guid?     SalesOrderLineId { get; set; }

    /// <summary>Mal kabulle gelen miktar (satır birimi).</summary>
    public decimal ReceivedQuantity { get; set; }

    /// <summary>Kısa kapatma ile serbest bırakılan açık miktar.</summary>
    public decimal ClosedQuantity { get; set; }

    // Satır toplamları (belge para birimi)
    public decimal Net   { get; set; }
    public decimal Vat   { get; set; }
    public decimal Total { get; set; }

    public Guid TenantId { get; set; }

    public decimal OpenQuantity => Math.Max(0m, Quantity - ReceivedQuantity - ClosedQuantity);
}
