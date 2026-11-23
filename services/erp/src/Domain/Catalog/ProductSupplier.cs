namespace ERP.Domain.Catalog;

public class ProductSupplier : BaseEntity, ITenantOwned {
    public Guid    ProductId        { get; set; }
    public Guid    CurrentAccountId { get; set; }
    public string? SupplierCode     { get; set; }
    public bool    IsDefault        { get; set; }
    public Guid    TenantId         { get; set; }
}
