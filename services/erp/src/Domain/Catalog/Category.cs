namespace ERP.Domain.Catalog;

public class Category : BaseEntity, ITenantOwned {
    public Guid?  ParentId { get; set; }
    public string Name     { get; set; } = string.Empty;
    public int    Sort     { get; set; }
    public Guid   TenantId { get; set; }
}
