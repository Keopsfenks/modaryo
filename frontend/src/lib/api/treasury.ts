import type { Currency } from "../money";
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
};

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
