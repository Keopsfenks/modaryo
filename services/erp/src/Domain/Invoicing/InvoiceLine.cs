namespace ERP.Domain.Invoicing;

public class InvoiceLine : BaseEntity, ITenantOwned {
    public Guid     InvoiceId       { get; set; }
    public int      Sort            { get; set; }
    public Guid?    ProductId       { get; set; }
    public Guid?    VariantId       { get; set; }
    public string   Description     { get; set; } = string.Empty;
    public string   Unit            { get; set; } = string.Empty;
    public decimal  Factor          { get; set; } = 1m;
    public decimal  Quantity        { get; set; }
    public decimal? Width           { get; set; }
    public decimal? Length          { get; set; }
    public decimal  UnitPrice       { get; set; }
    public bool     VatIncluded     { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountAmount  { get; set; }
    public int      VatRate         { get; set; }
    public string?  WithholdingCode { get; set; }
    public string?  ExemptionCode   { get; set; }
    public string?  ExemptionReason { get; set; }
    public decimal? OtvPercent      { get; set; }
    public decimal? OtvPerUnit      { get; set; }

    // CalcLineResult (belge para birimi)
    public decimal Gross        { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal DocDiscount  { get; set; }
    public decimal Net          { get; set; }
    public decimal Otv          { get; set; }
    public decimal VatBase      { get; set; }
    public decimal Vat          { get; set; }
    public decimal Withheld     { get; set; }
    public decimal Total        { get; set; }

    // TL karşılıkları
    public decimal NetTl      { get; set; }
    public decimal OtvTl      { get; set; }
    public decimal VatTl      { get; set; }
    public decimal WithheldTl { get; set; }
    public decimal TotalTl    { get; set; }

    public Guid?                      SourceLineId { get; set; }
    public IReadOnlyList<InvoiceLot>? Lots         { get; set; }
    public IReadOnlyList<string>?     Serials      { get; set; }
    public Guid                       TenantId     { get; set; }
}
