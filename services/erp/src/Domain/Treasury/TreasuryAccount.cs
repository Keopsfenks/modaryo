namespace ERP.Domain.Treasury;

/// <summary>Kasa, banka hesabı veya kart tahsilatı alan POS hesabı; bakiye hareketlerin toplamıdır.</summary>
public class TreasuryAccount : BaseAuditableEntity, ITenantOwned {
    public TreasuryAccountKind Kind     { get; set; }
    public string              Name     { get; set; } = string.Empty;
    public string              Currency { get; set; } = "TRY";
    public Guid?               BranchId { get; set; }
    public string?             Iban     { get; set; }
    public string?             BankName { get; set; }
    public bool                IsActive { get; set; } = true;
    public Guid                TenantId { get; set; }
}
