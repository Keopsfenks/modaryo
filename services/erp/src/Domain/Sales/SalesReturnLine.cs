namespace ERP.Domain.Sales;

public class SalesReturnLine : BaseEntity, ITenantOwned {
    public Guid     SalesReturnId    { get; set; }
    public int      Sort             { get; set; }
    public Guid     InvoiceLineId    { get; set; }
    public Guid?    SalesOrderLineId { get; set; }
    public Guid?    ProductId        { get; set; }
    public Guid?    VariantId        { get; set; }
    public string   Description      { get; set; } = string.Empty;
    public string   Unit             { get; set; } = string.Empty;
    public decimal  Quantity         { get; set; }
    public decimal? Width            { get; set; }
    public decimal? Length           { get; set; }
    public Guid     TenantId         { get; set; }
}
