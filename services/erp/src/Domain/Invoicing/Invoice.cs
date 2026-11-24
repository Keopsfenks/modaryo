namespace ERP.Domain.Invoicing;

public class Invoice : BaseAuditableEntity, ITenantOwned {
    public InvoiceKind       Kind             { get; set; }
    public InvoiceStatus     Status           { get; set; } = InvoiceStatus.Draft;
    public InvoiceMode       Mode             { get; set; } = InvoiceMode.Paper;
    public EInvoiceScenario? Scenario         { get; set; }
    public Guid              BranchId         { get; set; }
    public Guid?             SeriesId         { get; set; }
    public string?           Number           { get; set; }
    public string?           ExternalNumber   { get; set; }
    public Guid              CurrentAccountId { get; set; }
    public DateOnly          Date             { get; set; }
    public TimeOnly?         Time             { get; set; }
    public string            Currency         { get; set; } = "TRY";
    public decimal           Rate             { get; set; } = 1m;
    public RateType          RateType         { get; set; } = RateType.ForexBuying;
    public bool              RateOverridden   { get; set; }
    public decimal?          DocDiscountPercent { get; set; }
    public decimal?          DocDiscountAmount  { get; set; }

    // Toplamlar (belge para birimi)
    public decimal Gross    { get; set; }
    public decimal Discount { get; set; }
    public decimal Net      { get; set; }
    public decimal Otv      { get; set; }
    public decimal Vat      { get; set; }
    public decimal Withheld { get; set; }
    public decimal Payable  { get; set; }

    // Toplamlar (TL)
    public decimal GrossTl    { get; set; }
    public decimal DiscountTl { get; set; }
    public decimal NetTl      { get; set; }
    public decimal OtvTl      { get; set; }
    public decimal VatTl      { get; set; }
    public decimal WithheldTl { get; set; }
    public decimal PayableTl  { get; set; }

    public bool            AffectsStock      { get; set; }
    public Guid?           WarehouseId       { get; set; }
    public int             PaymentTermDays   { get; set; }
    public string?         Notes             { get; set; }
    public Guid?           ReturnOfInvoiceId { get; set; }
    public string?         CancelReason      { get; set; }
    public DateTimeOffset? CancelledAt       { get; set; }
    public Guid?           LedgerEntryId     { get; set; }

    /// <summary>"manual" | "sales-order" | "purchase-receipt" ...</summary>
    public string SourceType { get; set; } = "manual";

    public Guid? SourceId { get; set; }

    public List<InvoiceLine>    Lines       { get; set; } = new();
    public List<InvoiceDueLine> DueSchedule { get; set; } = new();

    /// <summary>Alıcı (satış) ya da satıcı (alış) bilgisi; kesimde dondurulur.</summary>
    public AddressSnapshot? BuyerOrSeller { get; set; }

    /// <summary>xmin eşzamanlılık belirteci.</summary>
    public uint Version { get; set; }

    public Guid TenantId { get; set; }
}
