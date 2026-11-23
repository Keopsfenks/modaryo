namespace ERP.Domain.Catalog;

public class Brand : BaseEntity, ITenantOwned {
    public string Name     { get; set; } = string.Empty;
    public Guid   TenantId { get; set; }
}
