import { apiFetch } from "./client";

// ERP servisi (services/erp) kasa/banka uç noktalarının tipli sarmalayıcıları (/api/erp/treasury/*).
// Enum'lar camelCase string, tarihler "YYYY-MM-DD", tutarlar JSON sayısıdır.

export type TreasuryAccountKind = "cash" | "bank" | "cardPos";
export type FinanceDocumentKind = "collection" | "payment" | "transfer" | "expense" | "income" | "instrumentEvent" | "journalVoucher";

export type TreasuryAccountDto = {
    id: string;
    kind: TreasuryAccountKind;
    name: string;
    currency: string;
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
};
