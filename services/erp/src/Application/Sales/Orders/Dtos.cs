using ERP.Application.Sales.Quotes;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Orders;

public sealed record OrderLineInput(
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
    Guid?       SupplierId,
    Guid?       WarehouseId
) {
    public QuoteLineInput ToQuoteLine() {
        return new QuoteLineInput(ProductId, VariantId, Unit, Quantity, Width, Length, UnitPrice, VatIncluded, DiscountPercent, VatRate, SupplyMode, Description, SupplierId);
    }
}

public sealed record OrderInput(
    Guid                          CurrentAccountId,
    Guid                          BranchId,
    DateOnly                      Date,
    string                        Currency,
    Guid?                         SalesRepUserId,
    decimal?                      DocDiscount,
    string?                       Notes,
    uint?                         Version,
    bool?                         RequiresPrepayment,
    decimal?                      PrepaymentPercent,
    decimal?                      PrepaymentAmount,
    IReadOnlyList<OrderLineInput> Lines
) {
    public QuoteInput ToQuoteInput() {
        return new QuoteInput(CurrentAccountId, BranchId, Date, null, Currency, SalesRepUserId, DocDiscount, Notes, Version, Lines.Select(l => l.ToQuoteLine()).ToList());
    }
}

public sealed record TimelineStepDto(string Key, string Label, string State, DateTimeOffset? At, string? Note);

public sealed record OrderLineDto(
    Guid                         Id,
    int                          Sort,
    Guid                         ProductId,
    string                       ProductCode,
    string                       ProductName,
    Guid?                        VariantId,
    string                       Description,
    string                       Unit,
    decimal                      Factor,
    decimal                      Quantity,
    decimal?                     Width,
    decimal?                     Length,
    decimal                      UnitPrice,
    bool                         VatIncluded,
    decimal?                     DiscountPercent,
    int                          VatRate,
    SupplyMode                   SupplyMode,
    Guid?                        WarehouseId,
    Guid?                        SupplierId,
    Guid?                        PurchaseOrderId,
    string?                      PurchaseOrderNumber,
    decimal                      Net,
    decimal                      Vat,
    decimal                      Total,
    decimal                      ShippedQuantity,
    decimal                      InvoicedQuantity,
    decimal                      ReturnedQuantity,
    decimal                      OpenQuantity,
    decimal?                     EstimatedUnitCost,
    decimal?                     MarginPercent,
    IReadOnlyList<TimelineStepDto> Timeline
);

public sealed record OrderReservationDto(Guid LineId, Guid WarehouseId, decimal Quantity, string Status, Guid? ExpectedReceiptSourceId);

public sealed record OrderShipmentRefDto(Guid Id, string Number, string Status, DateOnly Date);

public sealed record OrderInvoiceRefDto(Guid Id, string Number, string Status, DateOnly Date);

public sealed record PlanRowDto(int Sequence, DateOnly DueDate, decimal Amount, decimal Remaining);

public sealed record PlanDto(decimal DownPayment, decimal DownPaymentRemaining, int InstallmentCount, DateOnly FirstDueDate, IReadOnlyList<PlanRowDto> Rows);

public sealed record AdvanceDto(Guid DocumentId, string Number, DateOnly Date, decimal Amount, string Currency);

public sealed record OrderDto(
    Guid                           Id,
    string                         Number,
    SalesOrderStatus               Status,
    InvoiceState                   InvoiceState,
    Guid                           CurrentAccountId,
    string                         CustomerName,
    Guid                           BranchId,
    string                         Currency,
    Guid?                          SalesRepUserId,
    string?                        SalesRepName,
    Guid?                          QuoteId,
    DateOnly                       Date,
    decimal                        DocDiscount,
    string?                        Notes,
    string?                        CancelReason,
    bool                           RequiresPrepayment,
    decimal?                       PrepaymentPercent,
    decimal?                       PrepaymentAmount,
    decimal                        PrepaymentRequired,
    decimal                        AdvanceTotal,
    decimal                        Gross,
    decimal                        Discount,
    decimal                        Net,
    decimal                        Vat,
    decimal                        Total,
    decimal?                       EstimatedCost,
    decimal?                       MarginPercent,
    uint                           Version,
    IReadOnlyList<OrderLineDto>    Lines,
    IReadOnlyList<OrderReservationDto> Reservations,
    IReadOnlyList<OrderShipmentRefDto> Shipments,
    IReadOnlyList<OrderInvoiceRefDto>  Invoices,
    PlanDto?                       Plan,
    IReadOnlyList<AdvanceDto>      Advances,
    IReadOnlyList<string>          Warnings
);

public sealed record OrderRowDto(
    Guid             Id,
    string           Number,
    SalesOrderStatus Status,
    InvoiceState     InvoiceState,
    Guid             CurrentAccountId,
    string           CustomerName,
    DateOnly         Date,
    string           Currency,
    decimal          Total,
    string?          SalesRepName
);

public sealed record OrderListResult(IReadOnlyList<OrderRowDto> Items, int Total);

public sealed record PendingLineDto(
    Guid     OrderId,
    string   OrderNumber,
    Guid     CurrentAccountId,
    string   CustomerName,
    Guid     LineId,
    int      Sort,
    Guid     ProductId,
    string   ProductName,
    string   Unit,
    decimal  Quantity,
    decimal  ShippedQuantity,
    decimal  InvoicedQuantity,
    decimal  OpenToShip,
    decimal  OpenToInvoice
);

public sealed record PendingListResult(IReadOnlyList<PendingLineDto> Items, int Total);

public sealed record PlanRowInput(DateOnly DueDate, decimal Amount);

public sealed record PlanInput(decimal DownPayment, IReadOnlyList<PlanRowInput>? Rows, int? Count, DateOnly? FirstDueDate);

public sealed record ShortageDto(Guid LineId, int Sort, Guid ProductId, string Product, Guid WarehouseId, decimal Required, decimal Available);

public sealed record OrderPdfFile(byte[] Content, string FileName);
