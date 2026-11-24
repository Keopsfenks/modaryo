namespace ERP.Domain.Treasury;

/// <summary>Tahsilat, ödeme, virman, masraf/gelir fişi veya çek/senet hareketi; numara firma ve yıl başına önek + yıl + 6 hane.</summary>
public class FinanceDocument : BaseAuditableEntity, ITenantOwned {
    public FinanceDocumentKind Kind              { get; set; }
    public string              Number            { get; set; } = string.Empty;
    public DateOnly            Date              { get; set; }
    public PaymentMethod?      Method            { get; set; }
    public Guid?               AccountId         { get; set; }
    public Guid?               TargetAccountId   { get; set; }
    public Guid?               CurrentAccountId  { get; set; }
    public Guid?               InstrumentId      { get; set; }
    public Guid?               ExpenseCategoryId { get; set; }
    public decimal             Amount            { get; set; }
    public string              Currency          { get; set; } = "TRY";
    public decimal?            Rate              { get; set; }
    public bool                IsAdvance         { get; set; }
    public Guid?               SalesOrderId      { get; set; }
    public Guid?               PurchaseOrderId   { get; set; }
    public string?             Description       { get; set; }

    /// <summary>Masraf/gelir fişinde KDV oranı (tutar KDV dahildir) ve ekli belge.</summary>
    public decimal?            VatRate           { get; set; }
    public Guid?               AttachmentFileId  { get; set; }

    public DateTimeOffset?     CancelledAt       { get; set; }
    public string?             CancelledBy       { get; set; }
    public string?             CancelReason      { get; set; }

    public bool IsCancelled => CancelledAt is not null;

    public Guid TenantId { get; set; }
}
