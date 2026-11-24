namespace ERP.Domain.CurrentAccounts;

public enum AccountKind {
    Corporate,
    Individual
}

public enum LedgerEntryType {
    Opening,
    DebitNote,
    CreditNote
}

public enum LedgerEntrySource {
    Manual,
    Invoice
}

public enum DueDirection {
    Receivable,
    Payable
}

public enum AllocationKind {
    Auto,
    Manual,
    Invoice
}