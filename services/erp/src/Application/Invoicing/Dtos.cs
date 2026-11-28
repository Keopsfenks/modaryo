using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing;

public sealed record DueInput {
    public DateOnly DueDate { get; init; }
    public decimal  Amount  { get; init; }
}

public sealed record LineInput {
    public Guid?             ProductId       { get; init; }
    public Guid?             VariantId       { get; init; }
    public string?           Description     { get; init; }
    public string?           Unit            { get; init; }
    public decimal           Quantity        { get; init; }
    public decimal?          Width           { get; init; }
    public decimal?          Length          { get; init; }
    public decimal           UnitPrice       { get; init; }
    public bool              VatIncluded     { get; init; }
    public decimal?          DiscountPercent { get; init; }
    public decimal?          DiscountAmount  { get; init; }
    public int?              VatRate         { get; init; }
    public string?           WithholdingCode { get; init; }
    public string?           ExemptionCode   { get; init; }
    public string?           ExemptionReason { get; init; }
    public decimal?          OtvPercent      { get; init; }
    public decimal?          OtvPerUnit      { get; init; }

    /// <summary>
    ///     Açık KDV tutarı: yalnızca sistem üretimi belgelerde (POS e-Arşiv satışı) kullanılır; KDV dahil raf fiyatından türetilen KDV'yi kuruşu kuruşuna
    ///     taşır. API girdisinde verilirse 400 (invalid_amounts).
    /// </summary>
    public decimal?          VatOverride     { get; init; }

    /// <summary>İade faturalarında iade edilen asıl fatura satırı.</summary>
    public Guid?             SourceLineId    { get; init; }
    public List<InvoiceLot>? Lots            { get; init; }

    /// <summary>Seri takipli kalemlerde satır miktarı kadar seri numarası.</summary>
    public List<string>?     Serials         { get; init; }
}

public sealed record InvoiceInput {
    public InvoiceKind     Kind               { get; init; }
    public Guid            BranchId           { get; init; }
    public Guid            CurrentAccountId   { get; init; }
    public DateOnly        Date               { get; init; }
    public TimeOnly?       Time               { get; init; }
    public string          Currency           { get; init; } = "TRY";
    public decimal?        Rate               { get; init; }
    public string?         ExternalNumber     { get; init; }
    public decimal?        DocDiscountPercent { get; init; }
    public decimal?        DocDiscountAmount  { get; init; }
    public bool            AffectsStock       { get; init; }
    public Guid?           WarehouseId        { get; init; }
    public int?            PaymentTermDays    { get; init; }
    public List<DueInput>? DueSchedule        { get; init; }
    public string?         Notes              { get; init; }
    public Guid?           ReturnOfInvoiceId  { get; init; }
    public List<LineInput> Lines              { get; init; } = new();

    /// <summary>Güncellemede istemcinin gördüğü sürüm (xmin); verilirse eşleşmeyen 409 version_conflict.</summary>
    public uint?           Version            { get; init; }
}

public sealed record LineDto(
    Guid                      Id,
    int                       Sort,
    Guid?                     ProductId,
    Guid?                     VariantId,
    string                    Description,
    string                    Unit,
    decimal                   Factor,
    decimal                   Quantity,
    decimal?                  Width,
    decimal?                  Length,
    decimal                   UnitPrice,
    bool                      VatIncluded,
    decimal?                  DiscountPercent,
    decimal?                  DiscountAmount,
    int                       VatRate,
    string?                   WithholdingCode,
    string?                   ExemptionCode,
    string?                   ExemptionReason,
    decimal?                  OtvPercent,
    decimal?                  OtvPerUnit,
    decimal                   Gross,
    decimal                   LineDiscount,
    decimal                   DocDiscount,
    decimal                   Net,
    decimal                   Otv,
    decimal                   VatBase,
    decimal                   Vat,
    decimal                   Withheld,
    decimal                   Total,
    decimal                   NetTl,
    decimal                   OtvTl,
    decimal                   VatTl,
    decimal                   WithheldTl,
    decimal                   TotalTl,
    Guid?                     SourceLineId,
    IReadOnlyList<InvoiceLot>? Lots,
    IReadOnlyList<string>?    Serials
);

public sealed record DueDto(DateOnly DueDate, decimal Amount);

public sealed record VatSummaryDto(int Rate, decimal Base, decimal Vat);

public sealed record TotalsDto(
    decimal Gross,
    decimal Discount,
    decimal Net,
    decimal Otv,
    decimal Vat,
    decimal Withheld,
    decimal Payable
);

public sealed record InvoiceDto(
    Guid                      Id,
    InvoiceKind               Kind,
    InvoiceStatus             Status,
    InvoiceMode               Mode,
    EInvoiceScenario?         Scenario,
    Guid                      BranchId,
    Guid?                     SeriesId,
    string?                   Number,
    string?                   ExternalNumber,
    Guid                      CurrentAccountId,
    string                    CurrentAccountName,
    DateOnly                  Date,
    TimeOnly?                 Time,
    string                    Currency,
    decimal                   Rate,
    RateType                  RateType,
    bool                      RateOverridden,
    decimal?                  DocDiscountPercent,
    decimal?                  DocDiscountAmount,
    TotalsDto                 Totals,
    TotalsDto                 TotalsTl,
    bool                      AffectsStock,
    Guid?                     WarehouseId,
    int                       PaymentTermDays,
    string?                   Notes,
    Guid?                     ReturnOfInvoiceId,
    string?                   CancelReason,
    DateTimeOffset?           CancelledAt,
    string                    SourceType,
    uint                      Version,
    AddressSnapshot?          BuyerOrSeller,
    IReadOnlyList<LineDto>    Lines,
    IReadOnlyList<DueDto>     DueSchedule,
    IReadOnlyList<VatSummaryDto> VatSummary
);

public sealed record InvoiceListItem(
    Guid          Id,
    InvoiceKind   Kind,
    InvoiceStatus Status,
    InvoiceMode   Mode,
    Guid          BranchId,
    string?       Number,
    string?       ExternalNumber,
    Guid          CurrentAccountId,
    string        CurrentAccountName,
    DateOnly      Date,
    string        Currency,
    decimal       Net,
    decimal       Vat,
    decimal       Payable,
    decimal       PayableTl
);

public sealed record InvoiceListTotals(decimal NetTl, decimal VatTl, decimal PayableTl);

public sealed record InvoiceListResult(IReadOnlyList<InvoiceListItem> Items, int Total, InvoiceListTotals Totals);

/// <summary>Önizleme: kaydedilmeden hesaplanan toplamlar ve TL karşılıkları.</summary>
public sealed record PreviewDto(
    decimal                      Rate,
    DateOnly?                    RateDate,
    bool                         RateOverridden,
    IReadOnlyList<LineDto>       Lines,
    TotalsDto                    Totals,
    TotalsDto                    TotalsTl,
    IReadOnlyList<VatSummaryDto> VatSummary,
    string                       CariCurrency,
    decimal                      LedgerAmount
);
