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
    IReadOnlyList<ShipmentLineInput> Lines,
    bool                             Draft = false
);

public sealed record DeliverInput(string? ReceivedBy, DateTimeOffset? DeliveredAt, Guid? ProofFileId, string? Note);

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
    IReadOnlyList<string>?     Serials,
    bool                       IsService
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
    IReadOnlyList<ShipmentLineDto> Lines,
    string?                        CarrierCode,
    string?                        TrackingNo,
    string?                        IrsaliyeNo,
    DateTimeOffset?                DispatchAt,
    string?                        Vehicle,
    string?                        Driver,
    Guid?                          PickListId,
    decimal                        TotalWeightKg,
    decimal                        TotalDesi,
    IReadOnlyList<ParcelDto>       Parcels,
    IReadOnlyList<AttemptDto>      Attempts,
    string?                        CarrierName,
    string?                        TrackingUrl,
    bool                           CustomerEmailMissing,
    Guid?                          ReturnId,
    ProofDto?                      Proof
);

public sealed record ProofDto(string ReceivedBy, DateTimeOffset DeliveredAt, Guid? PhotoFileId, string? Note);

public sealed record ParcelContentDto(Guid ShipmentLineId, decimal Quantity);

public sealed record ParcelDto(Guid Id, int Index, decimal WeightKg, decimal WidthCm, decimal LengthCm, decimal HeightCm, decimal Desi, IReadOnlyList<ParcelContentDto> Contents);

public sealed record AttemptDto(int AttemptNo, DateTimeOffset SentAt, string Outcome, DateTimeOffset? ResolvedAt, string? FailureReason);

/// <summary>Liste satırındaki kalem özeti (ilk 3 satır); toplam satır sayısı LineCount'tadır.</summary>
public sealed record ShipmentRowItemDto(string Description, string Unit, decimal Quantity, bool IsService);

public sealed record ShipmentRowDto(
    Guid                              Id,
    string                            Number,
    ShipmentStatus                    Status,
    Guid                              SalesOrderId,
    string                            OrderNumber,
    Guid                              CurrentAccountId,
    string                            CustomerName,
    DateOnly                          Date,
    Guid?                             InvoiceId,
    DateTimeOffset?                   ShippedAt,
    DateTimeOffset?                   DeliveredAt,
    string?                           CarrierCode,
    string?                           CarrierName,
    string?                           TrackingNo,
    string?                           TrackingUrl,
    string?                           IrsaliyeNo,
    Guid?                             PickListId,
    int                               LineCount,
    int                               ParcelCount,
    IReadOnlyList<ShipmentRowItemDto> Items,
    bool                              CustomerEmailMissing,
    DateTimeOffset                    Created
);

public sealed record ShipmentListResult(IReadOnlyList<ShipmentRowDto> Items, int Total);
