namespace ERP.Domain.Catalog;

public class ProductVariant : BaseEntity, ITenantOwned {
    public Guid                       ProductId             { get; set; }
    public string                     Code                  { get; set; } = string.Empty;
    public string?                    Barcode               { get; set; }
    public Dictionary<string, string> Values                { get; set; } = new();
    public Money4?                    SalePriceOverride     { get; set; }
    public Money4?                    PurchasePriceOverride { get; set; }
    public bool                       IsActive              { get; set; } = true;
    public Guid                       TenantId              { get; set; }
}
