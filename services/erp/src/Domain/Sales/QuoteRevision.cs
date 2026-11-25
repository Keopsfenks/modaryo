namespace ERP.Domain.Sales;

/// <summary>Teklifin bir revizyonu; gönderilmiş revizyonların satırları salt okunur saklanır.</summary>
public class QuoteRevision : BaseEntity, ITenantOwned {
    public Guid      QuoteId     { get; set; }
    public int       Revision    { get; set; } = 1;
    public DateOnly  Date        { get; set; }
    public DateOnly? ValidUntil  { get; set; }
    public decimal   DocDiscount { get; set; }
    public string?   Notes       { get; set; }

    public decimal Gross    { get; set; }
    public decimal Discount { get; set; }
    public decimal Net      { get; set; }
    public decimal Vat      { get; set; }
    public decimal Total    { get; set; }

    public List<QuoteLine> Lines { get; set; } = new();
    public Guid TenantId { get; set; }
}
