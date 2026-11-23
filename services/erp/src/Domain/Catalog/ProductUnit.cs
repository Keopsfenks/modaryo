namespace ERP.Domain.Catalog;

public class ProductUnit : BaseEntity, ITenantOwned {
    public Guid    ProductId { get; set; }
    public string  Unit      { get; set; } = string.Empty;

    /// <summary>1 {Unit} = Factor x temel birim.</summary>
    public decimal Factor    { get; set; }
    public string? Barcode   { get; set; }
    public Guid    TenantId  { get; set; }
}
