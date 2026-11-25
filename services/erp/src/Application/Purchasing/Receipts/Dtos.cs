using ERP.Application.Inventory.Ledger;
using ERP.Domain.Purchasing;

namespace ERP.Application.Purchasing.Receipts;

public sealed record ReceiptLineInput(
    Guid?                    PoLineId,
    Guid                     ProductId,
    Guid?                    VariantId,
    string?                  Unit,
    decimal                  Quantity,
    decimal?                 Width,
    decimal?                 Length,
    decimal?                 UnitPrice,
    IReadOnlyList<LotInput>? Lots,
    IReadOnlyList<string>?   Serials
);

public sealed record ReceiptInput(
    Guid?                       PurchaseOrderId,
    Guid?                       SupplierId,
    Guid                        BranchId,
    Guid                        WarehouseId,
    DateOnly                    Date,
    string?                     DeliveryNoteNo,
    string?                     Notes,
    string?                     Currency,
    IReadOnlyList<ReceiptLineInput> Lines
);

public sealed record ReceiptLineDto(
    Guid            Id,
    int             Sort,
    Guid?           PoLineId,
    Guid            ProductId,
    string          ProductCode,
    string          ProductName,
    Guid?           VariantId,
    string          Unit,
    decimal         Quantity,
    decimal         BaseQuantity,
    decimal?        UnitPrice,
    decimal?        UnitCostTl,
    Guid            WarehouseId,
    QuarantineState Quarantine,
    Guid?           TargetWarehouseId
);

public sealed record ReceiptDto(
    Guid                       Id,
    string                     Number,
    GoodsReceiptStatus         Status,
    Guid?                      PurchaseOrderId,
    string?                    PurchaseOrderNumber,
    Guid                       SupplierId,
    string                     SupplierName,
    Guid                       BranchId,
    Guid                       WarehouseId,
    DateOnly                   Date,
    string                     Currency,
    decimal                    Rate,
    string?                    DeliveryNoteNo,
    string?                    Notes,
    string?                    CancelReason,
    decimal?                   Total,
    bool                       Invoiced,
    IReadOnlyList<string>      Warnings,
    IReadOnlyList<ReceiptLineDto> Lines
);

public sealed record ReceiptRowDto(
    Guid               Id,
    string             Number,
    GoodsReceiptStatus Status,
    Guid?              PurchaseOrderId,
    string?            PurchaseOrderNumber,
    Guid               SupplierId,
    string             SupplierName,
    Guid               BranchId,
    Guid               WarehouseId,
    DateOnly           Date,
    string             Currency,
    decimal?           Total,
    int                LineCount
);

public sealed record ReceiptListResult(IReadOnlyList<ReceiptRowDto> Items, int Total);
