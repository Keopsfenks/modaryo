namespace ERP.Domain.Pricing;

public class CustomerGroup : BaseEntity, ITenantOwned {
    public string Name            { get; set; } = string.Empty;
    public Guid?  SalePriceListId { get; set; }
    public Guid   TenantId        { get; set; }
}
