using ERP.Domain.Invoicing;
using ERP.Domain.Shipping;

namespace ERP.Application.Shipping.Picking;

public sealed record PickSourceDto(Guid ShipmentLineId, decimal Quantity);

public sealed record PickLineDto(
    Guid                       Id,
    int                        Sort,
    Guid                       WarehouseId,
    Guid                       ProductId,
    Guid?                      VariantId,
    string                     Code,
    string                     Description,
    string                     Unit,
    decimal                    Factor,
    decimal                    Quantity,
    decimal?                   PickedQuantity,
    string?                    Location,
    IReadOnlyList<PickLot>?    SuggestedLots,
    IReadOnlyList<PickLot>?    PickedLots,
    IReadOnlyList<string>?     PickedSerials,
    IReadOnlyList<PickSource>? Sources
);

public sealed record PickListShipmentDto(Guid Id, string Number, string OrderNumber, string CustomerName);

public sealed record PickListDto(
    Guid                              Id,
    string                            Number,
    PickListStatus                    Status,
    Guid                              BranchId,
    Guid                              WarehouseId,
    string                            WarehouseName,
    IReadOnlyList<PickListShipmentDto> Shipments,
    uint                              Version,
    IReadOnlyList<PickLineDto>        Lines
);

/// <summary>Satır güncellemesi: toplanan miktar; lot/seri verilmezse lot FEFO önerisinden türetilir.</summary>
public sealed record PickLineInput(decimal PickedQty, IReadOnlyList<InvoiceLot>? Lots, IReadOnlyList<string>? Serials);

public sealed record CompletePickInput(bool Force);

public sealed record BatchPickInput(IReadOnlyList<Guid> ShipmentIds);
