namespace ERP.Domain.CurrentAccounts;

public class LedgerEntry : BaseAuditableEntity, ITenantOwned {
    public Guid              CurrentAccountId { get; set; }
    public LedgerEntryType   Type             { get; set; }
    public LedgerEntrySource Source           { get; set; } = LedgerEntrySource.Manual;
    public Guid?             SourceId         { get; set; }
    public DateOnly          Date             { get; set; }
    public DateOnly?         DueDate          { get; set; }
    public string?           DocumentNo       { get; set; }
    public string?           Description      { get; set; }
    public decimal           Debit            { get; set; }
    public decimal           Credit           { get; set; }
    public string            Currency         { get; set; } = "TRY";

    /// <summary>Hareket belge para biriminden çevrilmişse özgün para birimi, tutar ve kur.</summary>
    public string?  SourceCurrency { get; set; }
    public decimal? SourceAmount   { get; set; }
    public decimal? Rate           { get; set; }
    public DateTimeOffset?   CancelledAt      { get; set; }
    public string?           CancelledBy      { get; set; }
    public string?           CancelReason     { get; set; }

    public bool IsCancelled => CancelledAt is not null;

    public decimal Signed   => Debit - Credit;
    public Guid    TenantId { get; set; }
}