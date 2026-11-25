namespace ERP.Domain.Sales;

/// <summary>Satış temsilcisi x ay hedefi (TRY).</summary>
public class SalesTarget : BaseAuditableEntity, ITenantOwned {
    public Guid    UserId   { get; set; }
    public string? UserName { get; set; }
    public int     Year     { get; set; }
    public int     Month    { get; set; }
    public decimal Amount   { get; set; }
    public Guid    TenantId { get; set; }
}
