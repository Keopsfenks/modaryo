using ERP.Domain.Inventory;

namespace ERP.Application.Inventory.Ledger;

/// <summary>Defter satırı. BaseQuantity ürünün temel biriminde ve pozitiftir; yön, çağrılan yöntemden gelir.</summary>
public sealed record StockLine(
    Guid                    WarehouseId,
    Guid                    ProductId,
    Guid?                   VariantId,
    decimal                 BaseQuantity,
    decimal?                UnitCost,
    IReadOnlyList<LotInput>? Lots,
    IReadOnlyList<string>?  Serials,
    Guid?                   SourceLineId,
    bool                    ExemptFromStockCheck = false
);

public sealed record LotInput(string LotNo, DateOnly? Expiry, decimal Quantity, Guid? LotId);

public sealed record ReserveLine(
    Guid    WarehouseId,
    Guid    ProductId,
    Guid?   VariantId,
    decimal BaseQuantity,
    Guid    SourceLineId,
    bool    ExemptFromStockCheck,
    Guid?   ExpectedReceiptSourceId
);

/// <summary>Reserved yalnızca stoğu karşılanmış (bekleyen girişe bağlı olmayan) rezervasyonları sayar; Available yalnızca Normal depoda sıfırdan büyüktür.</summary>
public sealed record StockBalance(
    Guid     WarehouseId,
    Guid     ProductId,
    Guid?    VariantId,
    decimal  Physical,
    decimal  Reserved,
    decimal  Available,
    decimal? AverageCost
);

internal sealed record BelowMinimumData(Guid WarehouseId, Guid ProductId, Guid? VariantId, decimal Available, decimal MinQty);
