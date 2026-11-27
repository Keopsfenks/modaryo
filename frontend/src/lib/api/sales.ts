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
/** Sunucu durumları diğer tüm enum'lar gibi camelCase gelir. */
export type OrderRef<S extends string = string> = { id: string; number: string; status: S; date: string };

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
    shipments: OrderRef<ShipmentStatus>[];
    invoices: OrderRef<"draft" | "issued" | "cancelled">[];
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

export type TrackedLot = { lotNo: string; expiry: string | null; quantity: number; lotId?: string | null };

export type ShipmentInput = {
    orderId: string;
    warehouseId?: string | null;
    date?: string | null;
    deliveryAddressId?: string | null;
    cargoNote?: string | null;
    notes?: string | null;
    lines: { orderLineId: string; quantity: number; lots?: TrackedLot[]; serials?: string[] }[];
};

export type ShipmentLineDto = {
    id: string;
    sort: number;
    salesOrderLineId: string;
    productId: string;
    variantId: string | null;
    description: string;
    unit: string;
    factor: number;
    quantity: number;
    width: number | null;
    length: number | null;
    invoicedQuantity: number;
};

export type ShipmentDto = {
    id: string;
    number: string;
    status: ShipmentStatus;
    salesOrderId: string;
    orderNumber: string;
    currentAccountId: string;
    customerName: string;
    branchId: string;
    warehouseId: string;
    date: string;
    shippedAt: string | null;
    deliveredAt: string | null;
    receivedBy: string | null;
    cargoNote: string | null;
    notes: string | null;
    cancelReason: string | null;
    invoiceId: string | null;
    version: number;
    lines: ShipmentLineDto[];
};

export type ShipmentRow = {
    id: string;
    number: string;
    status: ShipmentStatus;
    salesOrderId: string;
    orderNumber: string;
    currentAccountId: string;
    customerName: string;
    date: string;
    invoiceId: string | null;
};

export type ReturnWarehouseKind = "normal" | "quarantine" | "damaged";
export const RETURN_KIND_LABELS: Record<ReturnWarehouseKind, string> = { normal: "Normal depo", quarantine: "Karantina", damaged: "Hasarlı" };

export type ReturnInput = {
    invoiceId: string;
    warehouseKind: ReturnWarehouseKind;
    warehouseId: string;
    date?: string | null;
    reason?: string | null;
    lines: { invoiceLineId: string; quantity: number; lots?: TrackedLot[]; serials?: string[] }[];
};

export type ReturnRow = {
    id: string;
    number: string;
    status: string;
    invoiceId: string;
    invoiceNumber: string | null;
    currentAccountId: string;
    customerName: string;
    date: string;
};

export type ReturnDto = ReturnRow & {
    salesOrderId: string | null;
    warehouseId: string;
    warehouseKind: ReturnWarehouseKind;
    reason: string | null;
    returnInvoiceId: string | null;
    lines: { id: string; sort: number; invoiceLineId: string; description: string; unit: string; quantity: number }[];
};

export type RecurrenceInterval = "monthly" | "quarterly" | "yearly";
export type RecurringMode = "draft" | "autoIssue";
export const INTERVAL_LABELS: Record<RecurrenceInterval, string> = { monthly: "Aylık", quarterly: "Üç aylık", yearly: "Yıllık" };
export const RECURRING_MODE_LABELS: Record<RecurringMode, string> = { draft: "Taslak oluştur", autoIssue: "Otomatik kes" };

export type RecurringLineInput = {
    productId?: string | null;
    variantId?: string | null;
    description?: string | null;
    unit?: string | null;
    quantity: number;
    unitPrice: number;
    vatIncluded: boolean;
    discountPercent?: number | null;
    vatRate?: number | null;
    fixedPrice: boolean;
};

export type RecurringInput = {
    name?: string | null;
    currentAccountId: string;
    branchId: string;
    currency: string;
    interval: RecurrenceInterval;
    dayOfMonth: number;
    startDate: string;
    endDate?: string | null;
    mode: RecurringMode;
    notes?: string | null;
    lines: RecurringLineInput[];
};

export type RecurringRow = {
    id: string;
    number: string;
    name: string;
    currentAccountId: string;
    currentAccountName: string;
    currency: string;
    interval: RecurrenceInterval;
    dayOfMonth: number;
    startDate: string;
    endDate: string | null;
    mode: RecurringMode;
    isActive: boolean;
    total: number;
    nextPeriod: string | null;
    lastPeriod: string | null;
};

export type RecurringDto = RecurringRow & {
    branchId: string;
    notes: string | null;
    net: number;
    vat: number;
    lines: (RecurringLineInput & { id: string; sort: number; description: string; unit: string; vatRate: number })[];
};

export type RecurringRun = { id: string; period: string; invoiceId: string; invoiceNumber: string | null; invoiceStatus: string; payable: number | null; generatedAt: string };

export type TargetRow = { userId: string; userName: string | null; month: number; amountTry: number };
export type TargetsDto = { year: number; rows: TargetRow[] };
export type TargetActualRow = { userId: string; userName: string | null; month: number; targetTry: number; actualTry: number; percent: number | null };
export type TargetActualsDto = { year: number; rows: TargetActualRow[] };

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
        list: (q: { status?: string; orderId?: string; currentAccountId?: string; search?: string; page?: number; pageSize?: number } = {}) =>
            apiFetch<Paged<ShipmentRow>>(`${BASE}/shipments${qs({ ...q })}`),
        get: (id: string) => apiFetch<ShipmentDto>(`${BASE}/shipments/${id}`),
        create: (body: ShipmentInput) => apiFetch<ShipmentDto>(`${BASE}/shipments`, { method: "POST", body }),
        deliver: (id: string, body: { deliveredAt?: string | null; receivedBy?: string | null } = {}) => apiFetch<ShipmentDto>(`${BASE}/shipments/${id}/deliver`, { method: "POST", body }),
        cancel: (id: string, reason: string) => apiFetch<ShipmentDto>(`${BASE}/shipments/${id}/cancel`, { method: "POST", body: { reason } }),
        invoiceDraft: (shipmentIds: string[], date?: string) => apiFetch<InvoiceDto>(`${BASE}/shipments/invoice-draft`, { method: "POST", body: { shipmentIds, date: date ?? null } }),
    },
    returns: {
        list: (q: { status?: string; invoiceId?: string; currentAccountId?: string; page?: number; pageSize?: number } = {}) => apiFetch<Paged<ReturnRow>>(`${BASE}/returns${qs({ ...q })}`),
        get: (id: string) => apiFetch<ReturnDto>(`${BASE}/returns/${id}`),
        create: (body: ReturnInput) => apiFetch<ReturnDto>(`${BASE}/returns`, { method: "POST", body }),
        cancel: (id: string, reason: string) => apiFetch<ReturnDto>(`${BASE}/returns/${id}/cancel`, { method: "POST", body: { reason } }),
    },
    recurring: {
        list: (q: { active?: boolean; currentAccountId?: string } = {}) => apiFetch<RecurringRow[]>(`${BASE}/recurring${qs({ ...q })}`),
        get: (id: string) => apiFetch<RecurringDto>(`${BASE}/recurring/${id}`),
        create: (body: RecurringInput) => apiFetch<RecurringDto>(`${BASE}/recurring`, { method: "POST", body }),
        update: (id: string, body: RecurringInput) => apiFetch<RecurringDto>(`${BASE}/recurring/${id}`, { method: "PUT", body }),
        pause: (id: string) => apiFetch<RecurringDto>(`${BASE}/recurring/${id}/pause`, { method: "POST" }),
        resume: (id: string) => apiFetch<RecurringDto>(`${BASE}/recurring/${id}/resume`, { method: "POST" }),
        runs: (id: string) => apiFetch<RecurringRun[]>(`${BASE}/recurring/${id}/runs`),
    },
    targets: {
        get: (year: number) => apiFetch<TargetsDto>(`${BASE}/targets${qs({ year })}`),
        put: (year: number, rows: { userId: string; month: number; amountTry: number }[]) => apiFetch<TargetsDto>(`${BASE}/targets${qs({ year })}`, { method: "PUT", body: { rows } }),
        actuals: (year: number) => apiFetch<TargetActualsDto>(`${BASE}/targets/actuals${qs({ year })}`),
    },
    settings: () => apiFetch<{ version: number; value: SalesSettings }>("/api/erp/settings/satis"),
};
