namespace ERP.Domain.Invoicing;

public enum InvoiceKind { Sales, Purchase, SalesReturn, PurchaseReturn, ExchangeDifference }

public enum InvoiceStatus { Draft, Issued, Cancelled }

public enum InvoiceMode { Paper, EInvoice, EArchive }

public enum EInvoiceScenario { Basic, Commercial }

public enum RateType { ForexBuying, ForexSelling, BanknoteBuying, BanknoteSelling }
