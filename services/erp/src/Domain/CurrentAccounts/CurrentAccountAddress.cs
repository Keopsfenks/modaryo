namespace ERP.Domain.CurrentAccounts;

public class CurrentAccountAddress : BaseEntity, ITenantOwned {
    public Guid    CurrentAccountId  { get; set; }
    public string  Title             { get; set; } = string.Empty;
    public string  Line              { get; set; } = string.Empty;
    public string? District          { get; set; }
    public string? City              { get; set; }
    public string? PostalCode        { get; set; }
    public string  Country           { get; set; } = "Türkiye";
    public bool    IsBilling         { get; set; }
    public bool    IsShipping        { get; set; }
    public bool    IsDefaultBilling  { get; set; }
    public bool    IsDefaultShipping { get; set; }
    public Guid    TenantId          { get; set; }
}