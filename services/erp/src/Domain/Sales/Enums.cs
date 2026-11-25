namespace ERP.Domain.Sales;

public enum SupplyMode { Stock, BackToBack }

public enum QuoteStatus { Draft, Sent, Accepted, Rejected, Expired }

public enum SalesOrderStatus { Draft, AwaitingLimitApproval, AwaitingPayment, Approved, PartiallyShipped, Shipped, Closed, Cancelled }

/// <summary>Siparişin faturalanma durumu (sipariş durumundan ayrı izlenir).</summary>
public enum InvoiceState { None, Partial, Full }

public enum ShipmentStatus { Draft, Shipped, Delivered, Cancelled }

public enum SalesReturnStatus { Draft, Posted, Cancelled }

public enum RecurrenceInterval { Monthly, Quarterly, Yearly }

/// <summary>Draft: taslak fatura üretir; AutoIssue: otomatik keser.</summary>
public enum RecurringMode { Draft, AutoIssue }
