using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Shipments;

public sealed record ShipmentLineInput(Guid OrderLineId, decimal Quantity, IReadOnlyList<InvoiceLot>? Lots, IReadOnlyList<string>? Serials);

public sealed record ShipmentInput(
    Guid                             OrderId,
    Guid?                            WarehouseId,
    DateOnly?                        Date,
    Guid?                            DeliveryAddressId,
    string?                          CargoNote,
    string?                          Notes,
    IReadOnlyList<ShipmentLineInput> Lines
);

public sealed record DeliverInput(DateTimeOffset? DeliveredAt, string? ReceivedBy);

public sealed record ShipmentLineDto(
    Guid                       Id,
    int                        Sort,
    Guid                       SalesOrderLineId,
    Guid                       ProductId,
    Guid?                      VariantId,
    string                     Description,
    string                     Unit,
    decimal                    Factor,
    decimal                    Quantity,
    decimal?                   Width,
    decimal?                   Length,
    decimal                    InvoicedQuantity,
    IReadOnlyList<InvoiceLot>? Lots,
    IReadOnlyList<string>?     Serials
);

public sealed record ShipmentDto(
    Guid                           Id,
    string                         Number,
    ShipmentStatus                 Status,
    Guid                           SalesOrderId,
    string                         OrderNumber,
    Guid                           CurrentAccountId,
    string                         CustomerName,
    Guid                           BranchId,
    Guid                           WarehouseId,
    DateOnly                       Date,
    DateTimeOffset?                ShippedAt,
    DateTimeOffset?                DeliveredAt,
    string?                        ReceivedBy,
    AddressSnapshot?               DeliveryAddress,
    string?                        CargoNote,
    string?                        Notes,
    string?                        CancelReason,
    Guid?                          InvoiceId,
    uint                           Version,
    IReadOnlyList<ShipmentLineDto> Lines
);

public sealed record ShipmentRowDto(
    Guid           Id,
    string         Number,
    ShipmentStatus Status,
    Guid           SalesOrderId,
    string         OrderNumber,
    Guid           CurrentAccountId,
    string         CustomerName,
    DateOnly       Date,
    Guid?          InvoiceId
);

public sealed record ShipmentListResult(IReadOnlyList<ShipmentRowDto> Items, int Total);
