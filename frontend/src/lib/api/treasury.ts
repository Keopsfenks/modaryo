import type { Currency } from "../money";
import { download } from "./catalog";
import { apiFetch } from "./client";

// ERP servisi (services/erp) kasa/banka uç noktalarının tipli sarmalayıcıları (/api/erp/treasury/*).
// Enum'lar camelCase string, tarihler "YYYY-MM-DD", tutarlar JSON sayısıdır.

export type TreasuryAccountKind = "cash" | "bank" | "cardPos";
export type FinanceDocumentKind = "collection" | "payment" | "transfer" | "expense" | "income" | "instrumentEvent" | "journalVoucher";

export type TreasuryAccountDto = {
    id: string;
    kind: TreasuryAccountKind;
    name: string;
    currency: Currency;
    branchId: string | null;
    iban: string | null;
    bankName: string | null;
    balance: number;
    isActive: boolean;
};

export type TreasuryAccountInput = {
    kind: TreasuryAccountKind;
    name: string;
    currency: string;
    branchId: string;
    iban?: string | null;
    bankName?: string | null;
};

export type MovementRow = {
    id: string;
    date: string;
    documentId: string;
    documentKind: FinanceDocumentKind | null;
    documentNumber: string | null;
    description: string | null;
    amount: number;
    balance: number;
    isCancelled: boolean;
};

export type AccountMovements = {
    account: TreasuryAccountDto;
    openingBalance: number;
    closingBalance: number;
    total: number;
    rows: MovementRow[];
};

const base = "/api/erp/treasury";

const get = <T>(path: string) => apiFetch<T>(`${base}${path}`);
const send = <T = void>(method: string, path: string, body?: unknown) => apiFetch<T>(`${base}${path}`, { method, body });

export const treasury = {
    accounts: () => get<TreasuryAccountDto[]>("/accounts"),
    account: (id: string) => get<TreasuryAccountDto>(`/accounts/${id}`),
    createAccount: (body: TreasuryAccountInput) => send<TreasuryAccountDto>("POST", "/accounts", body),
    updateAccount: (id: string, body: TreasuryAccountInput) => send<TreasuryAccountDto>("PUT", `/accounts/${id}`, body),
    setAccountActive: (id: string, active: boolean) => send("POST", `/accounts/${id}/${active ? "activate" : "deactivate"}`),
    deleteAccount: (id: string) => send("DELETE", `/accounts/${id}`),
    movements: (id: string, q: { from?: string; to?: string; page?: number; pageSize?: number } = {}) => {
        const search = new URLSearchParams();
        for (const [key, value] of Object.entries(q)) {
            if (value !== undefined && value !== "") search.set(key, String(value));
        }
        const text = search.toString();
        return get<AccountMovements>(`/accounts/${id}/movements${text ? `?${text}` : ""}`);
    },
    createCollection: (body: PaymentInput) => send<PostResult>("POST", "/collections", body),
    createPayment: (body: PaymentInput) => send<PostResult>("POST", "/payments", body),
    cancelDocument: (id: string, reason: string) => send("POST", `/finance-documents/${id}/cancel`, { reason }),
    openItems: (currentAccountId: string) => get<OpenItemForAllocation[]>(`/open-items?currentAccountId=${currentAccountId}`),
    allocate: (body: { creditLineId?: string; paymentId?: string; allocations: AllocationInput[] }) => send("POST", "/allocations", body),
    unallocate: (id: string) => send("DELETE", `/allocations/${id}`),
    createTransfer: (body: TransferInput) => send<PostResult>("POST", "/transfers", body),
    expenseCategories: () => get<ExpenseCategoryDto[]>("/expense-categories"),
    createExpenseCategory: (body: ExpenseCategoryInput) => send<ExpenseCategoryDto>("POST", "/expense-categories", body),
    updateExpenseCategory: (id: string, body: ExpenseCategoryInput) => send<ExpenseCategoryDto>("PUT", `/expense-categories/${id}`, body),
    deleteExpenseCategory: (id: string) => send("DELETE", `/expense-categories/${id}`),
    createExpense: (body: ExpenseInput) => send<ExpenseResult>("POST", "/expenses", body),

    // Çek/senet (cek-senet modülü)
    instruments: (q: InstrumentQuery = {}) => get<InstrumentDto[]>(`/instruments${query(q)}`),
    instrument: (id: string) => get<InstrumentDetail>(`/instruments/${id}`),
    portfolio: () => get<PortfolioRow[]>("/instruments/portfolio"),
    receiveInstrument: (body: InstrumentInput & { endorsementCount: number }) => send<InstrumentPostResult>("POST", "/instruments/receive", body),
    issueInstrument: (body: InstrumentInput & { bankAccountId?: string | null }) => send<InstrumentPostResult>("POST", "/instruments/issue", body),
    moveInstrument: (id: string, body: InstrumentMoveInput) => send<InstrumentPostResult>("POST", `/instruments/${id}/move`, body),

    // Kur farkı
    exchangeDifferences: (q: { currentAccountId?: string; from?: string; to?: string } = {}) => get<ExchangeDifferenceDto[]>(`/exchange-differences${query(q)}`),
    createDifferenceInvoice: (body: { allocationIds: string[]; vatRate?: number }) => send<{ invoiceId: string; amount: number; vatRate: number }>("POST", "/exchange-differences/invoice", body),
    createDifferenceExpense: (body: { allocationIds: string[] }) => send<{ expenseId: string; number: string; amount: number; kind: "Expense" | "Income" }>("POST", "/exchange-differences/expense", body),

    // Mahsup fişleri
    journalVouchers: (q: { from?: string; to?: string; includeCancelled?: boolean } = {}) => get<VoucherDto[]>(`/journal-vouchers${query(q)}`),
    createJournalVoucher: (body: VoucherInput) => send<{ id: string; number: string }>("POST", "/journal-vouchers", body),
    cancelJournalVoucher: (id: string, reason: string) => send("POST", `/journal-vouchers/${id}/cancel`, { reason }),

    // Raporlar
    aging: (q: { direction: "receivable" | "payable"; currency?: string; asOf?: string }) => get<AgingReport>(`/reports/aging${query(q)}`),
    cashFlow: (q: { granularity: "day" | "week"; periods?: number; from?: string }) => get<CashFlowReport>(`/reports/cash-flow${query(q)}`),
};

/** Yevmiye Excel çıktısı (/api/erp/accounting/journal.xlsx). */
export const exportJournal = (from: string, to: string) => download(`/api/erp/accounting/journal.xlsx${query({ from, to })}`, `yevmiye-${from}-${to}.xlsx`);

function query(q: object): string {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(q)) {
        if (value !== undefined && value !== "") search.set(key, String(value));
    }
    const text = search.toString();
    return text ? `?${text}` : "";
}

export type InstrumentKind = "check" | "note";
export type InstrumentDirection = "received" | "issued";
export type InstrumentStatus = "inPortfolio" | "endorsed" | "inCollection" | "collected" | "bounced" | "returnedToCustomer" | "issued" | "paid" | "returnedFromSupplier";

export type InstrumentDto = {
    id: string;
    kind: InstrumentKind;
    direction: InstrumentDirection;
    status: InstrumentStatus;
    number: string;
    dueDate: string;
    amount: number;
    currency: Currency;
    drawerName: string;
    bankName: string | null;
    bankBranch: string | null;
    accountNo: string | null;
    endorsementCount: number;
    currentAccountId: string;
    currentAccountName: string;
    endorsedToId: string | null;
    endorsedToName: string | null;
    collectionAccountId: string | null;
    protestFlag: boolean;
};

export type InstrumentEventDto = {
    id: string;
    from: InstrumentStatus | null;
    to: InstrumentStatus;
    date: string;
    note: string | null;
    documentId: string | null;
    documentNumber: string | null;
    created: string;
};

export type InstrumentDetail = { instrument: InstrumentDto; history: InstrumentEventDto[] };
export type PortfolioRow = { direction: InstrumentDirection; status: InstrumentStatus; currency: Currency; count: number; total: number };
export type InstrumentPostResult = { instrumentId: string; documentId: string; documentNumber: string; warnings: string[] };
export type InstrumentQuery = { direction?: InstrumentDirection; status?: InstrumentStatus; dueFrom?: string; dueTo?: string; currentAccountId?: string };

export type InstrumentInput = {
    kind: InstrumentKind;
    currentAccountId: string;
    number: string;
    dueDate: string;
    amount: number;
    currency: Currency;
    date: string;
    bankName?: string | null;
    bankBranch?: string | null;
    accountNo?: string | null;
    drawer?: string | null;
};

export type InstrumentMoveInput = { to: InstrumentStatus; date: string; currentAccountId?: string | null; accountId?: string | null; note?: string | null };

export type ExchangeDifferenceDto = {
    currentAccountId: string;
    currentAccountName: string;
    side: "receivable" | "payable";
    allocationIds: string[];
    invoiceNumber: string;
    foreignAmount: number;
    currency: Currency;
    invoiceRate: number;
    paymentRate: number;
    difference: number;
};

export type VoucherLineInput = { accountCode: string; debit: number; credit: number; description?: string | null; currentAccountId?: string | null };
export type VoucherInput = { date: string; description: string; lines: VoucherLineInput[] };
export type VoucherDto = {
    id: string;
    number: string;
    date: string;
    description: string;
    sourceType: string;
    sourceId: string | null;
    externalCaller: string | null;
    isCancelled: boolean;
    cancelReason: string | null;
    totalDebit: number;
    totalCredit: number;
    lines: { accountCode: string; debit: number; credit: number; description: string; currentAccountId: string | null }[];
};

export const AGING_BUCKETS = ["current", "1-30", "31-60", "61-90", "90+"] as const;
export type AgingBucket = (typeof AGING_BUCKETS)[number];
export type AgingRow = { currentAccountId: string; code: string; name: string; currency: Currency; buckets: Record<AgingBucket, number>; total: number };
export type AgingReport = {
    asOf: string;
    direction: "receivable" | "payable";
    rows: AgingRow[];
    totals: { currency: Currency; buckets: Record<AgingBucket, number>; total: number }[];
};

export type CashFlowSources = { dueLines: number; instruments: number; recurring: number; total: number };
export type CashFlowPeriod = { start: string; end: string; inflow: CashFlowSources; outflow: CashFlowSources; net: number; closing: number };
export type CashFlowReport = { start: string; granularity: "day" | "week"; currency: string; opening: number; periods: CashFlowPeriod[]; closing: number };

export type PaymentMethod = "cash" | "transfer" | "creditCard";
export type ExpenseKind = "expense" | "income";

export const METHOD_LABELS: Record<PaymentMethod, string> = {
    cash: "Nakit",
    transfer: "Havale / EFT",
    creditCard: "Kredi kartı",
};

export const ACCOUNT_KIND_LABELS: Record<TreasuryAccountKind, string> = {
    cash: "Kasa",
    bank: "Banka",
    cardPos: "Kart POS",
};

/** Belge gönderiminin sonucu; warnings içinde örneğin "cash_negative" gelebilir. */
export type PostResult = { id: string; number: string; warnings: string[] };
export type ExpenseResult = PostResult & { netAmount: number; vatAmount: number };

export type PaymentInput = {
    currentAccountId: string;
    accountId: string;
    method: PaymentMethod;
    date: string;
    amount: number;
    rate?: number | null;
    description?: string | null;
    isAdvance: boolean;
    allocations?: AllocationInput[];
};

export type AllocationInput = { dueLineId: string; amount: number };

export type OpenItemForAllocation = {
    dueLineId: string;
    entryId: string;
    source: "manual" | "invoice" | "payment" | "check" | "note" | "voucher";
    sourceId: string | null;
    documentNo: string | null;
    description: string | null;
    date: string;
    dueDate: string;
    sequence: number;
    direction: "receivable" | "payable";
    amount: number;
    open: number;
    allocatable: number;
};

export type TransferInput = {
    fromAccountId: string;
    toAccountId: string;
    date: string;
    amount: number;
    rate?: number | null;
    description?: string | null;
};

export type ExpenseCategoryDto = {
    id: string;
    code: string;
    name: string;
    accountCode: string;
    isIncome: boolean;
    isSystem: boolean;
    isActive: boolean;
};

export type ExpenseInput = {
    /** P0b dosya API'sine "attachment" olarak yüklenen belgenin kimliği. */
    attachmentFileId?: string | null;
    kind: ExpenseKind;
    categoryId: string;
    date: string;
    amount: number;
    vatRate: number | null;
    accountId?: string | null;
    currentAccountId?: string | null;
    description?: string | null;
};

export type ExpenseCategoryInput = { name: string; accountCode: string; isIncome?: boolean; isActive?: boolean };
