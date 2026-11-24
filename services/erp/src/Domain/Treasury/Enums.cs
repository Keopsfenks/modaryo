namespace ERP.Domain.Treasury;

public enum TreasuryAccountKind { Cash, Bank, CardPos }

public enum FinanceDocumentKind { Collection, Payment, Transfer, Expense, Income, InstrumentEvent, JournalVoucher }

public enum PaymentMethod { Cash, Transfer, CreditCard }

public enum InstrumentKind { Check, Note }

public enum InstrumentDirection { Received, Issued }

public enum InstrumentStatus {
    InPortfolio,
    Endorsed,
    InCollection,
    Collected,
    Bounced,
    ReturnedToCustomer,
    Issued,
    Paid,
    ReturnedFromSupplier
}
