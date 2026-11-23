namespace ERP.Domain.Catalog;

public class ProductImage : BaseEntity, ITenantOwned {
    public Guid ProductId { get; set; }
    public Guid FileId    { get; set; }
    public int  Sort      { get; set; }
    public Guid TenantId  { get; set; }
}
