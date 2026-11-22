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
    Manual
}

public enum DueDirection {
    Receivable,
    Payable
}