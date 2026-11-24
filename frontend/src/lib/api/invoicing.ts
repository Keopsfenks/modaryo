import { apiFetch } from "./client";

// ERP servisi fatura modülü uç noktalarının tipli sarmalayıcıları (/api/erp/document-series, /api/erp/settings/ebelge ...).
// Enum'lar camelCase string.

export type InvoiceMode = "paper" | "eInvoice" | "eArchive";
export type EInvoiceScenario = "basic" | "commercial";
export type SeriesGroup = "sales" | "purchase";

export type EBelgeSettings = { enabled: boolean; defaultScenario: EInvoiceScenario; eIrsaliyeEnabled: boolean };

export type SeriesDto = {
    id: string;
    kindGroup: SeriesGroup;
    branchId: string;
    mode: InvoiceMode;
    prefix: string;
    year: number;
    nextNumber: number;
    minWidth: number;
    isDefault: boolean;
    isActive: boolean;
    nextPreview: string;
};

export type SeriesInput = {
    branchId: string;
    kindGroup: SeriesGroup;
    mode: InvoiceMode;
    prefix: string;
    year?: number;
    minWidth?: number;
    isDefault?: boolean;
    isActive?: boolean;
};

export const invoicing = {
    series: {
        list: (branchId?: string) => apiFetch<SeriesDto[]>(`/api/erp/document-series${branchId ? `?branchId=${branchId}` : ""}`),
        create: (body: SeriesInput) => apiFetch<SeriesDto>("/api/erp/document-series", { method: "POST", body }),
        update: (id: string, body: Omit<SeriesInput, "branchId">) =>
            apiFetch<SeriesDto>(`/api/erp/document-series/${id}`, { method: "PUT", body }),
    },
};
