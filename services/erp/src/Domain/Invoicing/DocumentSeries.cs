namespace ERP.Domain.Invoicing;

public class DocumentSeries : BaseEntity, ITenantOwned {
    /// <summary>"sales" | "purchase"</summary>
    public string      KindGroup  { get; set; } = "sales";
    public Guid        BranchId   { get; set; }
    public InvoiceMode Mode       { get; set; }
    public string      Prefix     { get; set; } = string.Empty;
    public int         Year       { get; set; }
    public long        NextNumber { get; set; } = 1;
    public int         MinWidth   { get; set; } = 6;
    public bool        IsDefault  { get; set; }
    public bool        IsActive   { get; set; } = true;
    public Guid        TenantId   { get; set; }
}
