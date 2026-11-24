namespace ERP.Domain.Inventory;

public enum WarehouseKind { Normal, Quarantine, Damaged }

public enum TrackingMode { Lot, Serial }

public enum CostingMethod { WeightedAverage, Fifo }

public enum MovementType {
    Opening,
    Receipt,
    Issue,
    TransferOut,
    TransferIn,
    CountAdjustment,
    Revaluation,
    PurchaseReceipt,
    PurchaseReturn,
    SalesShipment,
    SalesReturn,
    BundleIssue
}

public enum ReservationStatus { Active, Released, Consumed }

public enum StockCountStatus { Open, Posted, Cancelled }
