using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Orders;

public sealed record PoLineInput(
    Guid      ProductId,
    Guid?     VariantId,
    string?   Unit,
    decimal   Quantity,
    decimal?  Width,
    decimal?  Length,
    decimal?  UnitPrice,
    decimal?  DiscountPercent,
    int?      VatRate,
    DateOnly? ExpectedDate,
    Guid?     SalesOrderLineId
);

public sealed record PoInput(
    Guid                     SupplierId,
    Guid                     BranchId,
    DateOnly                 Date,
    DateOnly?                ExpectedDate,
    string                   Currency,
    decimal?                 Rate,
    string?                  Notes,
    uint?                    Version,
    IReadOnlyList<PoLineInput> Lines
);

/// <summary>Son alış: ürünün kesilmiş alış faturalarındaki en son TL birim fiyatı (satır birimi cinsinden).</summary>
public sealed record LastPurchaseDto(decimal UnitPriceTl, DateOnly Date);

public sealed record PoLineDto(
    Guid              Id,
    int               Sort,
    Guid              ProductId,
    string            ProductCode,
    string            ProductName,
    Guid?             VariantId,
    string            Description,
    string            Unit,
    decimal           Factor,
    decimal           Quantity,
    decimal           ReceivedQuantity,
    decimal           ClosedQuantity,
    decimal           OpenQuantity,
    decimal?          Width,
    decimal?          Length,
    decimal?          UnitPrice,
    decimal?          DiscountPercent,
    int               VatRate,
    DateOnly?         ExpectedDate,
    Guid?             SalesOrderLineId,
    decimal?          Net,
    decimal?          Vat,
    decimal?          Total,
    LastPurchaseDto?  LastPurchase
);

public sealed record MatchRowDto(
    Guid     PoLineId,
    decimal  Ordered,
    decimal  Received,
    decimal  Invoiced,
    decimal? PoPrice,
    decimal? ReceiptPrice,
    decimal? InvoicePrice,
    bool     QtyMismatch,
    bool     PriceVariance
);

public sealed record PoReceiptRefDto(Guid Id, string Number, DateOnly Date, GoodsReceiptStatus Status);

public sealed record PoInvoiceRefDto(Guid Id, string? Number, DateOnly Date, string Status);

public sealed record PoDto(
    Guid                         Id,
    string                       Number,
    PurchaseOrderStatus          Status,
    Guid                         SupplierId,
    string                       SupplierName,
    Guid                         BranchId,
    DateOnly                     Date,
    DateOnly?                    ExpectedDate,
    string                       Currency,
    decimal                      Rate,
    string?                      Notes,
    string?                      CancelReason,
    string?                      CloseReason,
    decimal?                     Gross,
    decimal?                     Discount,
    decimal?                     Net,
    decimal?                     Vat,
    decimal?                     Total,
    uint                         Version,
    IReadOnlyList<PoLineDto>     Lines,
    IReadOnlyList<MatchRowDto>   MatchRows,
    IReadOnlyList<PoReceiptRefDto> Receipts,
    IReadOnlyList<PoInvoiceRefDto> Invoices
);

public sealed record PoRowDto(
    Guid                Id,
    string              Number,
    PurchaseOrderStatus Status,
    Guid                SupplierId,
    string              SupplierName,
    Guid                BranchId,
    DateOnly            Date,
    DateOnly?           ExpectedDate,
    string              Currency,
    decimal?            Total,
    int                 LineCount,
    bool                Overdue
);

public sealed record PoListResult(IReadOnlyList<PoRowDto> Items, int Total);

public sealed record PoPdfFile(byte[] Content, string FileName);
