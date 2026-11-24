namespace ERP.Domain.Treasury;

/// <summary>Hesap hareketi: tutar işaretlidir (giriş +, çıkış -) ve hesabın para birimindedir.</summary>
public class TreasuryMovement : BaseAuditableEntity, ITenantOwned {
    public Guid            AccountId   { get; set; }
    public Guid            DocumentId  { get; set; }
    public DateOnly        Date        { get; set; }
    public decimal         Amount      { get; set; }
    public string          Currency    { get; set; } = "TRY";
    public string?         Description { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public bool IsCancelled => CancelledAt is not null;

    public Guid TenantId { get; set; }
}
