import { ApiError, apiFetch, type ProblemBody } from "./client";
import type { PagedResult } from "./erp";

// ERP servisi fatura modülü uç noktalarının tipli sarmalayıcıları (/api/erp/invoices, /exchange-rates, /document-series ...).
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

export type InvoiceKind = "sales" | "purchase" | "salesReturn" | "purchaseReturn" | "exchangeDifference";
export type InvoiceStatus = "draft" | "issued" | "cancelled";
export type RateType = "forexBuying" | "forexSelling" | "banknoteBuying" | "banknoteSelling";

export const KIND_LABELS: Record<InvoiceKind, string> = {
    sales: "Satış faturası",
    purchase: "Alış faturası",
    salesReturn: "Satış iade faturası",
    purchaseReturn: "Alış iade faturası",
    exchangeDifference: "Kur farkı faturası",
};
export const STATUS_LABELS: Record<InvoiceStatus, string> = { draft: "Taslak", issued: "Kesildi", cancelled: "İptal" };
export const MODE_LABELS: Record<InvoiceMode, string> = { paper: "Kâğıt", eInvoice: "e-Fatura", eArchive: "e-Arşiv" };
export const isPurchaseKind = (kind: InvoiceKind): boolean => kind === "purchase" || kind === "purchaseReturn";

export type LineInput = {
    productId?: string | null;
    variantId?: string | null;
    description?: string | null;
    unit?: string | null;
    quantity: number;
    width?: number | null;
    length?: number | null;
    unitPrice: number;
    vatIncluded: boolean;
    discountPercent?: number | null;
    discountAmount?: number | null;
    vatRate?: number | null;
    withholdingCode?: string | null;
    exemptionCode?: string | null;
    exemptionReason?: string | null;
    sourceLineId?: string | null;
    lots?: { lotNo: string; expiry: string | null; quantity: number; lotId?: string | null }[] | null;
};

export type DueRowInput = { dueDate: string; amount: number };

export type InvoiceInput = {
    kind: InvoiceKind;
    branchId: string;
    currentAccountId: string;
    date: string;
    currency: string;
    rate?: number | null;
    externalNumber?: string | null;
    docDiscountPercent?: number | null;
    docDiscountAmount?: number | null;
    affectsStock: boolean;
    warehouseId?: string | null;
    paymentTermDays?: number | null;
    dueSchedule?: DueRowInput[] | null;
    notes?: string | null;
    returnOfInvoiceId?: string | null;
    lines: LineInput[];
    version?: number | null;
};

export type LineDto = LineInput & {
    id: string;
    sort: number;
    description: string;
    unit: string;
    factor: number;
    gross: number;
    lineDiscount: number;
    docDiscount: number;
    net: number;
    otv: number;
    vatBase: number;
    vat: number;
    withheld: number;
    total: number;
    totalTl: number;
    vatRate: number;
};

export type Totals = { gross: number; discount: number; net: number; otv: number; vat: number; withheld: number; payable: number };
export type VatSummaryRow = { rate: number; base: number; vat: number };

export type InvoiceDto = {
    id: string;
    kind: InvoiceKind;
    status: InvoiceStatus;
    mode: InvoiceMode;
    scenario: EInvoiceScenario | null;
    branchId: string;
    seriesId: string | null;
    number: string | null;
    externalNumber: string | null;
    currentAccountId: string;
    currentAccountName: string;
    date: string;
    currency: string;
    rate: number;
    rateOverridden: boolean;
    docDiscountPercent: number | null;
    docDiscountAmount: number | null;
    totals: Totals;
    totalsTl: Totals;
    affectsStock: boolean;
    warehouseId: string | null;
    paymentTermDays: number;
    notes: string | null;
    returnOfInvoiceId: string | null;
    cancelReason: string | null;
    version: number;
    lines: LineDto[];
    dueSchedule: { dueDate: string; amount: number }[];
    vatSummary: VatSummaryRow[];
};

export type PreviewDto = {
    rate: number;
    rateDate: string | null;
    rateOverridden: boolean;
    lines: LineDto[];
    totals: Totals;
    totalsTl: Totals;
    vatSummary: VatSummaryRow[];
    cariCurrency: string;
    ledgerAmount: number;
};

export type InvoiceListItem = {
    id: string;
    kind: InvoiceKind;
    status: InvoiceStatus;
    mode: InvoiceMode;
    number: string | null;
    externalNumber: string | null;
    currentAccountId: string;
    currentAccountName: string;
    date: string;
    currency: string;
    net: number;
    vat: number;
    payable: number;
    payableTl: number;
};

export type InvoiceListResult = PagedResult<InvoiceListItem> & { totals: { netTl: number; vatTl: number; payableTl: number } };

export type InvoiceListQuery = {
    kind?: InvoiceKind;
    status?: InvoiceStatus;
    currentAccountId?: string;
    from?: string;
    to?: string;
    search?: string;
    page?: number;
    pageSize?: number;
};

export type InvoiceCodes = {
    withholding: { code: string; name: string; tenths: number }[];
    exemption: { code: string; name: string; group: string }[];
};

export type RateDto = { currency: string; type: RateType; rate: number; effectiveDate: string; source: string; overridden: boolean };

export type DocumentSettings = {
    accentColor: string;
    footerNote: string | null;
    showIbans: boolean;
    invoiceEmailSubject: string;
    invoiceEmailBody: string;
};

function qs(params: Record<string, string | number | undefined>): string {
    const search = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== "") search.set(k, String(v));
    const text = search.toString();
    return text ? `?${text}` : "";
}

/** İkili (PDF) yanıt: apiFetch metne çevirdiği için ayrı okunur. */
export async function fetchBlob(path: string): Promise<Blob> {
    let response: Response;
    try {
        response = await fetch(new URL(path, window.location.origin).toString(), { credentials: "same-origin" });
    } catch {
        throw new ApiError(0, "Sunucuya ulaşılamadı. Bağlantınızı kontrol edin.");
    }
    if (!response.ok) {
        let problem: ProblemBody | null = null;
        try {
            problem = (await response.json()) as ProblemBody;
        } catch {
            problem = null;
        }
        throw ApiError.fromProblem(response.status, problem);
    }
    return response.blob();
}

export const invoicing = {
    list: (q: InvoiceListQuery = {}) => apiFetch<InvoiceListResult>(`/api/erp/invoices${qs({ ...q })}`),
    get: (id: string) => apiFetch<InvoiceDto>(`/api/erp/invoices/${id}`),
    create: (body: InvoiceInput) => apiFetch<InvoiceDto>("/api/erp/invoices", { method: "POST", body }),
    update: (id: string, body: InvoiceInput) => apiFetch<InvoiceDto>(`/api/erp/invoices/${id}`, { method: "PUT", body }),
    remove: (id: string) => apiFetch<void>(`/api/erp/invoices/${id}`, { method: "DELETE" }),
    preview: (body: InvoiceInput, signal?: AbortSignal) => apiFetch<PreviewDto>("/api/erp/invoices/preview", { method: "POST", body, signal }),
    issue: (id: string) => apiFetch<InvoiceDto>(`/api/erp/invoices/${id}/issue`, { method: "POST" }),
    cancel: (id: string, reason: string) => apiFetch<void>(`/api/erp/invoices/${id}/cancel`, { method: "POST", body: { reason } }),
    email: (id: string, body: { to: string[]; cc?: string[]; subject?: string; body?: string }) =>
        apiFetch<void>(`/api/erp/invoices/${id}/email`, { method: "POST", body }),
    codes: () => apiFetch<InvoiceCodes>("/api/erp/invoices/codes"),
    pdf: (id: string) => fetchBlob(`/api/erp/invoices/${id}/pdf`),
    samplePdf: () => fetchBlob("/api/erp/invoices/sample-pdf"),

    rates: {
        list: (date?: string) => apiFetch<RateDto[]>(`/api/erp/exchange-rates${qs({ date })}`),
        lookup: (currency: string, date: string, type?: RateType) =>
            apiFetch<RateDto>(`/api/erp/exchange-rates/lookup${qs({ currency, date, type })}`),
        setOverride: (body: { date: string; currency: string; type: RateType; rate: number }) =>
            apiFetch<RateDto>("/api/erp/exchange-rates/override", { method: "PUT", body }),
        fetchNow: () => apiFetch<{ stored: number; failed: number }>("/api/erp/exchange-rates/fetch", { method: "POST" }),
    },

    series: {
        list: (branchId?: string) => apiFetch<SeriesDto[]>(`/api/erp/document-series${branchId ? `?branchId=${branchId}` : ""}`),
        create: (body: SeriesInput) => apiFetch<SeriesDto>("/api/erp/document-series", { method: "POST", body }),
        update: (id: string, body: Omit<SeriesInput, "branchId">) =>
            apiFetch<SeriesDto>(`/api/erp/document-series/${id}`, { method: "PUT", body }),
    },
};
