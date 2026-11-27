import { apiFetch } from "./client";
import { fetchBlob, type InvoiceDto } from "./invoicing";

// ERP satış uç noktalarının tipli sarmalayıcıları (/api/erp/sales/*). Enum'lar camelCase string.
// Toplamlar sunucudan gelir; istemci vergi hesabı yapmaz (önizleme için /invoices/preview kullanılır).

const BASE = "/api/erp/sales";

export type SupplyMode = "stock" | "backToBack";
export type QuoteStatus = "draft" | "sent" | "accepted" | "rejected" | "expired";
export type OrderStatus = "draft" | "awaitingLimitApproval" | "awaitingPayment" | "approved" | "partiallyShipped" | "shipped" | "closed" | "cancelled";
export type InvoiceState = "none" | "partial" | "full";
export type ShipmentStatus = "draft" | "shipped" | "delivered" | "cancelled";

export const QUOTE_STATUS_LABELS: Record<QuoteStatus, string> = {
    draft: "Taslak",
    sent: "Gönderildi",
    accepted: "Kabul edildi",
    rejected: "Reddedildi",
    expired: "Süresi doldu",
};

export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
    draft: "Taslak",
    awaitingLimitApproval: "Limit onayı bekliyor",
    awaitingPayment: "Ön ödeme bekliyor",
    approved: "Onaylı",
    partiallyShipped: "Kısmen sevk edildi",
    shipped: "Sevk edildi",
    closed: "Kapatıldı",
    cancelled: "İptal",
};

export const INVOICE_STATE_LABELS: Record<InvoiceState, string> = { none: "Faturalanmadı", partial: "Kısmen faturalandı", full: "Faturalandı" };
export const SHIPMENT_STATUS_LABELS: Record<ShipmentStatus, string> = { draft: "Taslak", shipped: "Sevk edildi", delivered: "Teslim edildi", cancelled: "İptal" };

export type SalesLineInput = {
    productId: string;
    variantId?: string | null;
    unit?: string | null;
    quantity: number;
    width?: number | null;
    length?: number | null;
    unitPrice?: number | null;
    vatIncluded?: boolean | null;
    discountPercent?: number | null;
    vatRate?: number | null;
    supplyMode?: SupplyMode | null;
    description?: string | null;
    supplierId?: string | null;
    warehouseId?: string | null;
};

export type QuoteInput = {
    currentAccountId: string;
    branchId: string;
    date: string;
    validUntil?: string | null;
    currency: string;
    salesRepUserId?: string | null;
    docDiscount?: number | null;
    notes?: string | null;
    version?: number | null;
    lines: SalesLineInput[];
};

export type OrderInput = Omit<QuoteInput, "validUntil"> & {
    requiresPrepayment?: boolean | null;
    prepaymentPercent?: number | null;
    prepaymentAmount?: number | null;
};

export type SalesLineDto = {
    id: string;
    sort: number;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    description: string;
    unit: string;
    factor: number;
    quantity: number;
    width: number | null;
    length: number | null;
    unitPrice: number;
    vatIncluded: boolean;
    discountPercent: number | null;
    vatRate: number;
    supplyMode: SupplyMode;
    supplierId: string | null;
    net: number;
    vat: number;
    total: number;
};

export type QuoteRevisionRef = { revision: number; date: string; validUntil: string | null; total: number };

export type QuoteDto = {
    id: string;
    number: string;
    status: QuoteStatus;
    currentAccountId: string;
    customerName: string;
    branchId: string;
    currency: string;
    salesRepUserId: string | null;
    salesRepName: string | null;
    currentRevision: number;
    revision: number;
    date: string;
    validUntil: string | null;
    docDiscount: number;
    notes: string | null;
    sentAt: string | null;
    rejectReason: string | null;
    salesOrderId: string | null;
    gross: number;
    discount: number;
    net: number;
    vat: number;
    total: number;
    version: number;
    lines: SalesLineDto[];
    revisions: QuoteRevisionRef[];
};

export type QuoteRow = {
    id: string;
    number: string;
    status: QuoteStatus;
    currentAccountId: string;
    customerName: string;
    date: string;
    validUntil: string | null;
    currency: string;
    total: number;
    salesRepName: string | null;
    revision: number;
};

export type TimelineStep = { key: string; label: string; state: "done" | "current" | "pending"; at: string | null; note: string | null };

export type OrderLineDto = Omit<SalesLineDto, "supplierId"> & {
    warehouseId: string | null;
    supplierId: string | null;
    purchaseOrderId: string | null;
    purchaseOrderNumber: string | null;
    shippedQuantity: number;
    invoicedQuantity: number;
    returnedQuantity: number;
    openQuantity: number;
    /** maliyet.read izni yoksa null. */
    estimatedUnitCost: number | null;
    marginPercent: number | null;
    timeline: TimelineStep[];
};

export type PlanRowDto = { sequence: number; dueDate: string; amount: number; remaining: number };
export type PlanDto = { downPayment: number; downPaymentRemaining: number; installmentCount: number; firstDueDate: string; rows: PlanRowDto[] };
export type AdvanceDto = { documentId: string; number: string; date: string; amount: number; currency: string };
export type OrderRef = { id: string; number: string; status: string; date: string };

export type OrderDto = {
    id: string;
    number: string;
    status: OrderStatus;
    invoiceState: InvoiceState;
    currentAccountId: string;
    customerName: string;
    branchId: string;
    currency: string;
    salesRepUserId: string | null;
    salesRepName: string | null;
    quoteId: string | null;
    date: string;
    docDiscount: number;
    notes: string | null;
    cancelReason: string | null;
    requiresPrepayment: boolean;
    prepaymentPercent: number | null;
    prepaymentAmount: number | null;
    prepaymentRequired: number;
    advanceTotal: number;
    gross: number;
    discount: number;
    net: number;
    vat: number;
    total: number;
    estimatedCost: number | null;
    marginPercent: number | null;
    version: number;
    lines: OrderLineDto[];
    reservations: { lineId: string; warehouseId: string; quantity: number; status: string; expectedReceiptSourceId: string | null }[];
    shipments: OrderRef[];
    invoices: OrderRef[];
    plan: PlanDto | null;
    advances: AdvanceDto[];
    warnings: string[];
};

export type OrderRow = {
    id: string;
    number: string;
    status: OrderStatus;
    invoiceState: InvoiceState;
    currentAccountId: string;
    customerName: string;
    date: string;
    currency: string;
    total: number;
    salesRepName: string | null;
};

export type PendingLine = {
    orderId: string;
    orderNumber: string;
    currentAccountId: string;
    customerName: string;
    lineId: string;
    sort: number;
    productId: string;
    productName: string;
    unit: string;
    quantity: number;
    shippedQuantity: number;
    invoicedQuantity: number;
    openToShip: number;
    openToInvoice: number;
};

export type PlanInput = {
    downPayment: number;
    rows?: { dueDate: string; amount: number }[];
    count?: number;
    firstDueDate?: string;
};

export type ShipmentInput = {
    orderId: string;
    warehouseId?: string | null;
    date?: string | null;
    cargoNote?: string | null;
    notes?: string | null;
    lines: { orderLineId: string; quantity: number }[];
};

export type ShipmentDto = { id: string; number: string; status: ShipmentStatus; orderNumber: string; date: string };

export type SalesSettings = { quoteValidityDays: number; requireSalesRep: boolean; defaultInstallmentCount: number; prepaymentDefaultOff: boolean };

type Paged<T> = { items: T[]; total: number };

function qs(params: Record<string, string | number | boolean | undefined>): string {
    const search = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== "") search.set(k, String(v));
    const text = search.toString();
    return text ? `?${text}` : "";
}

export type ListQuery = { status?: string; currentAccountId?: string; search?: string; page?: number; pageSize?: number };

export const sales = {
    quotes: {
        list: (q: ListQuery = {}) => apiFetch<Paged<QuoteRow>>(`${BASE}/quotes${qs({ ...q })}`),
        get: (id: string, revision?: number) => apiFetch<QuoteDto>(`${BASE}/quotes/${id}${qs({ revision })}`),
        create: (body: QuoteInput) => apiFetch<QuoteDto>(`${BASE}/quotes`, { method: "POST", body }),
        update: (id: string, body: QuoteInput) => apiFetch<QuoteDto>(`${BASE}/quotes/${id}`, { method: "PUT", body }),
        send: (id: string, body: { emailTo?: string[]; subject?: string; body?: string } = {}) => apiFetch<QuoteDto>(`${BASE}/quotes/${id}/send`, { method: "POST", body }),
        accept: (id: string) => apiFetch<{ orderId: string; orderNumber: string }>(`${BASE}/quotes/${id}/accept`, { method: "POST" }),
        reject: (id: string, reason: string) => apiFetch<QuoteDto>(`${BASE}/quotes/${id}/reject`, { method: "POST", body: { reason } }),
        pdf: (id: string, revision?: number) => fetchBlob(`${BASE}/quotes/${id}/pdf${qs({ revision })}`),
    },
    orders: {
        list: (q: ListQuery = {}) => apiFetch<Paged<OrderRow>>(`${BASE}/orders${qs({ ...q })}`),
        pending: (q: { currentAccountId?: string; search?: string; toShip?: boolean; toInvoice?: boolean; page?: number; pageSize?: number } = {}) =>
            apiFetch<Paged<PendingLine>>(`${BASE}/orders/pending${qs({ ...q })}`),
        get: (id: string) => apiFetch<OrderDto>(`${BASE}/orders/${id}`),
        create: (body: OrderInput) => apiFetch<OrderDto>(`${BASE}/orders`, { method: "POST", body }),
        update: (id: string, body: OrderInput) => apiFetch<OrderDto>(`${BASE}/orders/${id}`, { method: "PUT", body }),
        approve: (id: string) => apiFetch<OrderDto>(`${BASE}/orders/${id}/approve`, { method: "POST" }),
        approveLimit: (id: string) => apiFetch<OrderDto>(`${BASE}/orders/${id}/approve-limit`, { method: "POST" }),
        cancel: (id: string, reason: string) => apiFetch<OrderDto>(`${BASE}/orders/${id}/cancel`, { method: "POST", body: { reason } }),
        close: (id: string, reason?: string) => apiFetch<OrderDto>(`${BASE}/orders/${id}/close`, { method: "POST", body: { reason } }),
        setPlan: (id: string, body: PlanInput) => apiFetch<OrderDto>(`${BASE}/orders/${id}/installment-plan`, { method: "PUT", body }),
        quickInvoice: (id: string, warehouseId?: string | null) =>
            apiFetch<{ shipmentId: string; invoiceId: string; invoice: InvoiceDto }>(`${BASE}/orders/${id}/quick-invoice`, { method: "POST", body: { warehouseId: warehouseId ?? null } }),
        proforma: (id: string) => fetchBlob(`${BASE}/orders/${id}/proforma.pdf`),
    },
    shipments: {
        create: (body: ShipmentInput) => apiFetch<ShipmentDto>(`${BASE}/shipments`, { method: "POST", body }),
    },
    settings: () => apiFetch<{ version: number; value: SalesSettings }>("/api/erp/settings/satis"),
};
