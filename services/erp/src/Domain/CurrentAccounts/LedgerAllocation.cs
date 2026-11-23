namespace ERP.Domain.CurrentAccounts;

/// <summary>Bir alacak (ödeme) satırının bir borç satırını kapattığı tutar.</summary>
public class LedgerAllocation : BaseEntity, ITenantOwned {
    public Guid           CurrentAccountId { get; set; }
    public Guid           DebitLineId      { get; set; }
    public Guid           CreditLineId     { get; set; }
    public decimal        Amount           { get; set; }
    public AllocationKind Kind             { get; set; }
    public DateTimeOffset CreatedAt        { get; set; }
    public Guid           TenantId         { get; set; }
}
