namespace ERP.Domain.Purchasing;

public enum PurchaseOrderStatus { Draft, Approved, PartiallyReceived, Received, Closed, Cancelled }

public enum GoodsReceiptStatus { Posted, Cancelled }

/// <summary>None: karantina yok; Held: karantinada; Released: depoya alındı; Rejected: reddedildi.</summary>
public enum QuarantineState { None, Held, Released, Rejected }

public enum AllocationKey { Amount, Quantity, Weight }

public enum LandedCostStatus { Draft, Posted, Cancelled }

public enum Incoterm { EXW, FCA, FOB, CIF, DAP, DDP }

public enum ImportFileStatus { Open, Closed }

/// <summary>İthalat dosyasına bağlanan belge türü.</summary>
public enum ImportLinkKind { PurchaseOrder, GoodsReceipt, PurchaseInvoice, LandedCost, Attachment }
