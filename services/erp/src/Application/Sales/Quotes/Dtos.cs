using ERP.Domain.Sales;

namespace ERP.Application.Sales.Quotes;

public sealed record QuoteLineInput(
    Guid        ProductId,
    Guid?       VariantId,
    string?     Unit,
    decimal     Quantity,
    decimal?    Width,
    decimal?    Length,
    decimal?    UnitPrice,
    bool?       VatIncluded,
    decimal?    DiscountPercent,
    int?        VatRate,
    SupplyMode? SupplyMode,
    string?     Description,
    Guid?       SupplierId
);

public sealed record QuoteInput(
    Guid                         CurrentAccountId,
    Guid                         BranchId,
    DateOnly                     Date,
    DateOnly?                    ValidUntil,
    string                       Currency,
    Guid?                        SalesRepUserId,
    decimal?                     DocDiscount,
    string?                      Notes,
    uint?                        Version,
    IReadOnlyList<QuoteLineInput> Lines
);

public sealed record QuoteLineDto(
    Guid       Id,
    int        Sort,
    Guid       ProductId,
    string     ProductCode,
    string     ProductName,
    Guid?      VariantId,
    string     Description,
    string     Unit,
    decimal    Factor,
    decimal    Quantity,
    decimal?   Width,
    decimal?   Length,
    decimal    UnitPrice,
    bool       VatIncluded,
    decimal?   DiscountPercent,
    int        VatRate,
    SupplyMode SupplyMode,
    Guid?      SupplierId,
    decimal    Net,
    decimal    Vat,
    decimal    Total
);

public sealed record QuoteRevisionRefDto(int Revision, DateOnly Date, DateOnly? ValidUntil, decimal Total);

public sealed record QuoteDto(
    Guid                              Id,
    string                            Number,
    QuoteStatus                       Status,
    Guid                              CurrentAccountId,
    string                            CustomerName,
    Guid                              BranchId,
    string                            Currency,
    Guid?                             SalesRepUserId,
    string?                           SalesRepName,
    int                               CurrentRevision,
    int                               Revision,
    DateOnly                          Date,
    DateOnly?                         ValidUntil,
    decimal                           DocDiscount,
    string?                           Notes,
    DateTimeOffset?                   SentAt,
    string?                           RejectReason,
    Guid?                             SalesOrderId,
    decimal                           Gross,
    decimal                           Discount,
    decimal                           Net,
    decimal                           Vat,
    decimal                           Total,
    uint                              Version,
    IReadOnlyList<QuoteLineDto>       Lines,
    IReadOnlyList<QuoteRevisionRefDto> Revisions
);

public sealed record QuoteRowDto(
    Guid        Id,
    string      Number,
    QuoteStatus Status,
    Guid        CurrentAccountId,
    string      CustomerName,
    DateOnly    Date,
    DateOnly?   ValidUntil,
    string      Currency,
    decimal     Total,
    string?     SalesRepName,
    int         Revision
);

public sealed record QuoteListResult(IReadOnlyList<QuoteRowDto> Items, int Total);

public sealed record QuotePdfFile(byte[] Content, string FileName);

public sealed record AcceptQuoteResult(Guid OrderId, string OrderNumber);
