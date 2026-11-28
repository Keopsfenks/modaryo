namespace ERP.Domain.Pos;

/// <summary>Perakende satış fişi. Numara: PS{kasaKodu}{yyyyMMdd}{0001}.</summary>
public class PosSale : BaseAuditableEntity, ITenantOwned {
    public string         Number           { get; set; } = string.Empty;
    public PosSaleStatus  Status           { get; set; } = PosSaleStatus.Completed;
    public Guid           RegisterId       { get; set; }
    public Guid           ShiftId          { get; set; }
    public Guid           CashierUserId    { get; set; }
    public Guid           BranchId         { get; set; }
    public Guid           WarehouseId      { get; set; }
    public Guid           CurrentAccountId { get; set; }
    public DateOnly       Date             { get; set; }
    public DateTimeOffset At               { get; set; }

    public LegalDocumentMode Mode { get; set; } = LegalDocumentMode.FiscalReceipt;

    public decimal Subtotal    { get; set; }
    public decimal Discount    { get; set; }
    public decimal VatTotal    { get; set; }
    public decimal Total       { get; set; }
    public decimal ChangeGiven { get; set; }

    /// <summary>Kasaya net giren nakit (para üstü düşülmüş), karttan çekilen, veresiye ve iade mahsubu tutarları.</summary>
    public decimal CashNet          { get; set; }
    public decimal CardAmount       { get; set; }
    public decimal OnAccountAmount  { get; set; }
    public decimal ReturnCreditAmount { get; set; }

    /// <summary>İstemcinin ürettiği satış kimliği: aynı kimlikle tekrar gelen gönderim yeni satış açmaz, kayıtlı satışı döndürür.</summary>
    public Guid ClientSaleId { get; set; }

    public string? CashierName { get; set; }

    /// <summary>e-Arşiv modunda üretilen fatura.</summary>
    public Guid? InvoiceId { get; set; }

    public string?         CancelReason      { get; set; }
    public DateTimeOffset? CancelledAt       { get; set; }
    public Guid?           CancelledByUserId { get; set; }

    public List<PosSaleLine> Lines    { get; set; } = new();
    public List<PosPayment>  Payments { get; set; } = new();

    public uint Version  { get; set; }
    public Guid TenantId { get; set; }
}
