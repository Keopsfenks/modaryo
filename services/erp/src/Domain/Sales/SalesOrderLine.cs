namespace ERP.Domain.Sales;

public class SalesOrderLine : BaseEntity, ITenantOwned {
    public Guid       SalesOrderId    { get; set; }
    public int        Sort            { get; set; }
    public Guid       ProductId       { get; set; }
    public Guid?      VariantId       { get; set; }
    public string     Description     { get; set; } = string.Empty;
    public string     Unit            { get; set; } = string.Empty;
    public decimal    Factor          { get; set; } = 1m;
    public decimal    Quantity        { get; set; }
    public decimal?   Width           { get; set; }
    public decimal?   Length          { get; set; }
    public decimal    UnitPrice       { get; set; }
    public bool       VatIncluded     { get; set; }
    public decimal?   DiscountPercent { get; set; }
    public int        VatRate         { get; set; }
    public SupplyMode SupplyMode      { get; set; } = SupplyMode.Stock;
    public Guid?      WarehouseId     { get; set; }
    public Guid?      SupplierId      { get; set; }
    public Guid?      QuoteLineId     { get; set; }

    /// <summary>Siparişe göre tedarik: oluşturulan satın alma siparişi ve satırı.</summary>
    public Guid? PurchaseOrderId     { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }

    public decimal Net   { get; set; }
    public decimal Vat   { get; set; }
    public decimal Total { get; set; }

    public decimal ShippedQuantity  { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }

    public Guid TenantId { get; set; }

    public decimal OpenQuantity => Math.Max(0m, Quantity - ShippedQuantity);
}
