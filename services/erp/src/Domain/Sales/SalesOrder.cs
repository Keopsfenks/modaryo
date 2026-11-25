namespace ERP.Domain.Sales;

public class SalesOrder : BaseAuditableEntity, ITenantOwned {
    public string           Number           { get; set; } = string.Empty;
    public SalesOrderStatus Status           { get; set; } = SalesOrderStatus.Draft;
    public InvoiceState     InvoiceState     { get; set; } = InvoiceState.None;
    public Guid             CurrentAccountId { get; set; }
    public Guid             BranchId         { get; set; }
    public DateOnly         Date             { get; set; }
    public string           Currency         { get; set; } = "TRY";
    public Guid?            SalesRepUserId   { get; set; }
    public string?          SalesRepName     { get; set; }
    public Guid?            QuoteId          { get; set; }
    public int?             QuoteRevision    { get; set; }
    public decimal          DocDiscount      { get; set; }
    public string?          Notes            { get; set; }
    public string?          CancelReason     { get; set; }

    // Ön ödeme
    public bool     RequiresPrepayment { get; set; }
    public decimal? PrepaymentPercent  { get; set; }
    public decimal? PrepaymentAmount   { get; set; }

    public decimal Gross    { get; set; }
    public decimal Discount { get; set; }
    public decimal Net      { get; set; }
    public decimal Vat      { get; set; }
    public decimal Total    { get; set; }

    public InstallmentPlan?     Plan  { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = new();

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint Version  { get; set; }
    public Guid TenantId { get; set; }

    public bool IsEditable => Status == SalesOrderStatus.Draft;
}
