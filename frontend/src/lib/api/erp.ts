import { apiFetch } from "./client";
import type { Currency } from "../money";

// ERP servisi (services/erp) cari hesap uç noktalarının tipli sarmalayıcıları.
// Yollar Next.js rewrites ile /api/erp/... adresine gider. Tarihler "YYYY-MM-DD", tutarlar JSON sayısıdır.

export type { Currency };

export type AccountKind = "corporate" | "individual";
export type AccountRole = "customer" | "supplier";
export type LedgerEntryType = "opening" | "debitNote" | "creditNote";
export type DueDirection = "receivable" | "payable";
export type EntryInputType = "opening" | "debit" | "credit";
export type EntryDirection = "debit" | "credit";

export type PagedResult<T> = { items: T[]; total: number };

export type CurrentAccountListItem = {
    id: string;
    code: string;
    name: string;
    isCustomer: boolean;
    isSupplier: boolean;
    currency: Currency;
    balance: number;
    overLimit: boolean;
    overdueAmount: number;
    isActive: boolean;
};

export type AddressDto = {
    id: string;
    title: string;
    line: string;
    district: string | null;
    city: string | null;
    postalCode: string | null;
    country: string;
    isBilling: boolean;
    isShipping: boolean;
    isDefaultBilling: boolean;
    isDefaultShipping: boolean;
};

export type CurrentAccountDto = {
    id: string;
    code: string;
    name: string;
    kind: AccountKind;
    taxNumber: string | null;
    taxOffice: string | null;
    isEInvoiceTaxpayer: boolean;
    isCustomer: boolean;
    isSupplier: boolean;
    phone: string | null;
    email: string | null;
    notes: string | null;
    currency: Currency;
    defaultDueDays: number;
    creditLimit: number | null;
    isActive: boolean;
    balance: number;
    overdueAmount: number;
    hasMovements: boolean;
    addresses: AddressDto[];
};

export type AddressInput = {
    title: string;
    line: string;
    district?: string | null;
    city?: string | null;
    postalCode?: string | null;
    country: string;
    isBilling: boolean;
    isShipping: boolean;
    isDefaultBilling?: boolean;
    isDefaultShipping?: boolean;
};

export type OpeningBalanceInput = { amount: number; direction: EntryDirection; date: string };

export type UpdateAccountInput = {
    code: string;
    name: string;
    kind: AccountKind;
    taxNumber?: string | null;
    taxOffice?: string | null;
    isEInvoiceTaxpayer: boolean;
    isCustomer: boolean;
    isSupplier: boolean;
    phone?: string | null;
    email?: string | null;
    notes?: string | null;
    currency: Currency;
    defaultDueDays: number;
    creditLimit?: number | null;
};

export type CreateAccountInput = UpdateAccountInput & {
    address?: AddressInput | null;
    openingBalance?: OpeningBalanceInput | null;
};

export type EntryInput = {
    type: EntryInputType;
    date: string;
    amount: number;
    documentNo?: string | null;
    description?: string | null;
    dueDate?: string | null;
    /** Yalnızca type "opening" iken zorunlu. */
    direction?: EntryDirection;
};

export type StatementRow = {
    id: string;
    date: string;
    type: LedgerEntryType;
    documentNo: string | null;
    description: string | null;
    debit: number;
    credit: number;
    dueDate: string | null;
    runningBalance: number;
    cancelled: boolean;
    cancelReason: string | null;
};

export type AddEntryResult = { entry: StatementRow; balance: number; creditLimitExceeded: boolean };

export type StatementResult = {
    openingBalance: number;
    rows: StatementRow[];
    totalDebit: number;
    totalCredit: number;
    closingBalance: number;
    currency: Currency;
};

export type OpenItem = {
    entryId: string;
    date: string;
    dueDate: string;
    direction: DueDirection;
    remaining: number;
    daysOverdue: number;
};

export type DueBucket = { overdue: number; within30: number; later: number };
export type CurrencyDueSummary = { currency: Currency; receivable: DueBucket; payable: DueBucket };
export type OverdueAccount = {
    id: string;
    code: string;
    name: string;
    currency: Currency;
    overdueReceivable: number;
    maxDaysOverdue: number;
};
export type DueSummary = { byCurrency: CurrencyDueSummary[]; topOverdue: OverdueAccount[] };

export type ListAccountsQuery = {
    search?: string;
    role?: AccountRole;
    includeInactive?: boolean;
    page?: number;
    pageSize?: number;
};

const base = "/api/erp/current-accounts";

/** Tanımsız değerleri atlayarak "?a=1&b=2" üretir; hiç param yoksa boş string. */
function queryString(params: Record<string, string | number | boolean | undefined>): string {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value !== undefined) {
            search.set(key, String(value));
        }
    }
    const text = search.toString();
    return text ? `?${text}` : "";
}

const get = <T>(path: string) => apiFetch<T>(`${base}${path}`);
const send = <T = void>(method: string, path: string, body?: unknown) => apiFetch<T>(`${base}${path}`, { method, body });

export const erp = {
    listAccounts: (q: ListAccountsQuery = {}) =>
        get<PagedResult<CurrentAccountListItem>>(
            queryString({
                search: q.search,
                role: q.role,
                includeInactive: q.includeInactive,
                page: q.page,
                pageSize: q.pageSize,
            }),
        ),
    nextCode: () => get<{ code: string }>("/next-code"),
    getAccount: (id: string) => get<CurrentAccountDto>(`/${id}`),
    createAccount: (body: CreateAccountInput) => send<CurrentAccountDto>("POST", "", body),
    updateAccount: (id: string, body: UpdateAccountInput) => send<CurrentAccountDto>("PUT", `/${id}`, body),
    setActive: (id: string, active: boolean) => send("POST", `/${id}/${active ? "activate" : "deactivate"}`),
    deleteAccount: (id: string) => send("DELETE", `/${id}`),

    addAddress: (id: string, a: AddressInput) => send<AddressDto>("POST", `/${id}/addresses`, a),
    updateAddress: (id: string, addressId: string, a: AddressInput) => send<AddressDto>("PUT", `/${id}/addresses/${addressId}`, a),
    deleteAddress: (id: string, addressId: string) => send("DELETE", `/${id}/addresses/${addressId}`),

    addEntry: (id: string, e: EntryInput) => send<AddEntryResult>("POST", `/${id}/entries`, e),
    cancelEntry: (id: string, entryId: string, reason: string) => send("POST", `/${id}/entries/${entryId}/cancel`, { reason }),
    statement: (id: string, from?: string, to?: string) => get<StatementResult>(`/${id}/statement${queryString({ from, to })}`),

    dueItems: (id: string) => get<OpenItem[]>(`/${id}/due-items`),
    dueSummary: () => get<DueSummary>("/due-summary"),
};
