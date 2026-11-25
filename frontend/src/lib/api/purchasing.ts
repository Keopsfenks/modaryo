import { apiFetch } from "./client";
import type { PagedResult } from "./erp";
import type { LotInput } from "./inventory";
import { fetchBlob, type InvoiceDto } from "./invoicing";

// ERP satın alma uç noktalarının tipli sarmalayıcıları (/api/erp/purchasing/*). Enum'lar camelCase string.
// fiyat.alis izni yoksa sunucu tüm fiyat ve tutar alanlarını null döner.

const BASE = "/api/erp/purchasing";

export type PoStatus = "draft" | "approved" | "partiallyReceived" | "received" | "closed" | "cancelled";
export type ReceiptStatus = "posted" | "cancelled";
export type QuarantineState = "none" | "held" | "released" | "rejected";

export const PO_STATUS_LABELS: Record<PoStatus, string> = {
    draft: "Taslak",
    approved: "Onaylı",
    partiallyReceived: "Kısmen geldi",
    received: "Tamamı geldi",
    closed: "Kapatıldı",
    cancelled: "İptal",
};

export const RECEIPT_STATUS_LABELS: Record<ReceiptStatus, string> = { posted: "Kabul edildi", cancelled: "İptal" };

export type PoLineInput = {
    productId: string;
    variantId?: string | null;
    unit?: string | null;
    quantity: number;
    width?: number | null;
    length?: number | null;
    unitPrice?: number | null;
    discountPercent?: number | null;
    vatRate?: number | null;
    expectedDate?: string | null;
    salesOrderLineId?: string | null;
};

export type PoInput = {
    supplierId: string;
    branchId: string;
    date: string;
    expectedDate?: string | null;
    currency: string;
    rate?: number | null;
    notes?: string | null;
    version?: number | null;
    lines: PoLineInput[];
};

export type LastPurchase = { unitPriceTl: number; date: string };

export type PoLineDto = {
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
    receivedQuantity: number;
    closedQuantity: number;
    openQuantity: number;
    width: number | null;
    length: number | null;
    unitPrice: number | null;
    discountPercent: number | null;
    vatRate: number;
    expectedDate: string | null;
    salesOrderLineId: string | null;
    net: number | null;
    vat: number | null;
    total: number | null;
    lastPurchase: LastPurchase | null;
};

export type MatchRow = {
    poLineId: string;
    ordered: number;
    received: number;
    invoiced: number;
    poPrice: number | null;
    receiptPrice: number | null;
    invoicePrice: number | null;
    qtyMismatch: boolean;
    priceVariance: boolean;
};

export type PoReceiptRef = { id: string; number: string; date: string; status: ReceiptStatus };
export type PoInvoiceRef = { id: string; number: string | null; date: string; status: string };

export type PoDto = {
    id: string;
    number: string;
    status: PoStatus;
    supplierId: string;
    supplierName: string;
    branchId: string;
    date: string;
    expectedDate: string | null;
    currency: string;
    rate: number;
    notes: string | null;
    cancelReason: string | null;
    closeReason: string | null;
    gross: number | null;
    discount: number | null;
    net: number | null;
    vat: number | null;
    total: number | null;
    version: number;
    lines: PoLineDto[];
    matchRows: MatchRow[];
    receipts: PoReceiptRef[];
    invoices: PoInvoiceRef[];
};

export type PoRow = {
    id: string;
    number: string;
    status: PoStatus;
    supplierId: string;
    supplierName: string;
    branchId: string;
    date: string;
    expectedDate: string | null;
    currency: string;
    total: number | null;
    lineCount: number;
    overdue: boolean;
};

export type PoQuery = { status?: PoStatus; supplierId?: string; overdue?: boolean; search?: string; page?: number; pageSize?: number };

export type ReceiptLineInput = {
    poLineId?: string | null;
    productId: string;
    variantId?: string | null;
    unit?: string | null;
    quantity: number;
    unitPrice?: number | null;
    lots?: LotInput[];
    serials?: string[];
};

export type ReceiptInput = {
    purchaseOrderId?: string | null;
    supplierId?: string | null;
    branchId: string;
    warehouseId: string;
    date: string;
    deliveryNoteNo?: string | null;
    notes?: string | null;
    currency?: string | null;
    lines: ReceiptLineInput[];
};

export type ReceiptLineDto = {
    id: string;
    sort: number;
    poLineId: string | null;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    unit: string;
    quantity: number;
    baseQuantity: number;
    unitPrice: number | null;
    unitCostTl: number | null;
    warehouseId: string;
    quarantine: QuarantineState;
    targetWarehouseId: string | null;
};

export type ReceiptDto = {
    id: string;
    number: string;
    status: ReceiptStatus;
    purchaseOrderId: string | null;
    purchaseOrderNumber: string | null;
    supplierId: string;
    supplierName: string;
    branchId: string;
    warehouseId: string;
    date: string;
    currency: string;
    rate: number;
    deliveryNoteNo: string | null;
    notes: string | null;
    cancelReason: string | null;
    total: number | null;
    invoiced: boolean;
    warnings: string[];
    lines: ReceiptLineDto[];
};

export type ReceiptRow = {
    id: string;
    number: string;
    status: ReceiptStatus;
    purchaseOrderId: string | null;
    purchaseOrderNumber: string | null;
    supplierId: string;
    supplierName: string;
    branchId: string;
    warehouseId: string;
    date: string;
    currency: string;
    total: number | null;
    lineCount: number;
};

export type ReceiptQuery = { purchaseOrderId?: string; supplierId?: string; status?: ReceiptStatus; search?: string; page?: number; pageSize?: number };

export type QuarantineRow = {
    receiptLineId: string;
    receiptId: string;
    receiptNumber: string;
    date: string;
    supplierName: string;
    purchaseOrderId: string | null;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    unit: string;
    receivedQuantity: number;
    openQuantity: number;
    openBaseQuantity: number;
    warehouseId: string;
    targetWarehouseId: string | null;
};

export type QuarantineResult = { receiptLineId: string; state: QuarantineState; openQuantity: number; returnId: string | null; invoiceId: string | null; invoiceNumber: string | null };

export type ReturnInput = {
    receiptId: string;
    lines: { receiptLineId: string; quantity: number; lots?: LotInput[]; serials?: string[] }[];
    date: string;
    reason: string;
    createInvoice: boolean;
};

export type ReturnResult = { returnId: string; receiptId: string; invoiceId: string | null; invoiceNumber: string | null };

export type PurchasingSettings = {
    overDeliveryPercent: number;
    priceVariancePercent: number;
    quarantineEnabled: boolean;
    quarantineWarehouseByBranch: Record<string, string>;
    defaultLandedKey: string;
};

export type PriceStat = { productId: string; variantId: string | null; unit: string; lastUnitPriceTl: number; minUnitPriceTl: number; maxUnitPriceTl: number; lastPurchaseDate: string };

export type AllocationKey = "amount" | "quantity" | "weight";
export type CostType = "navlun" | "gumruk" | "sigorta" | "diger";
export type LandedCostStatus = "draft" | "posted" | "cancelled";

export const ALLOCATION_KEY_LABELS: Record<AllocationKey, string> = { amount: "Tutar", quantity: "Miktar", weight: "Ağırlık" };
export const COST_TYPE_LABELS: Record<CostType, string> = { navlun: "Navlun", gumruk: "Gümrük", sigorta: "Sigorta", diger: "Diğer" };
export const COST_TYPES = Object.keys(COST_TYPE_LABELS) as CostType[];
export const LANDED_STATUS_LABELS: Record<LandedCostStatus, string> = { draft: "Taslak", posted: "İşlendi", cancelled: "İptal" };

export type LandedSourceInput = { invoiceId?: string | null; manualAmountTl?: number | null; description?: string | null; costType: CostType };

export type LandedCostInput = {
    date: string;
    key: AllocationKey;
    receiptIds?: string[];
    importFileId?: string | null;
    notes?: string | null;
    sources: LandedSourceInput[];
};

export type LandedSourceDto = { id: string; invoiceId: string | null; description: string; costType: CostType | null; amountTl: number };

export type LandedAllocationDto = {
    receiptId: string;
    receiptNumber: string;
    receiptLineId: string;
    productId: string;
    productCode: string;
    productName: string;
    baseQuantity: number;
    weightKgPerUnit: number | null;
    tlValue: number;
    allocatedTl: number;
    byCostType: Record<string, number>;
};

export type LandedCostDto = {
    id: string;
    number: string;
    status: LandedCostStatus;
    date: string;
    key: AllocationKey;
    importFileId: string | null;
    notes: string | null;
    cancelReason: string | null;
    totalTl: number;
    sources: LandedSourceDto[];
    allocations: LandedAllocationDto[];
    byCostType: Record<string, number>;
};

export type LandedCostRow = { id: string; number: string; status: LandedCostStatus; date: string; key: AllocationKey; totalTl: number; importFileId: string | null };

export type Incoterm = "EXW" | "FCA" | "FOB" | "CIF" | "DAP" | "DDP";
export const INCOTERMS: Incoterm[] = ["EXW", "FCA", "FOB", "CIF", "DAP", "DDP"];
export type ImportFileStatus = "open" | "closed";
export type ImportLinkKind = "purchaseOrder" | "goodsReceipt" | "purchaseInvoice" | "landedCost" | "attachment";
export const IMPORT_LINK_LABELS: Record<ImportLinkKind, string> = {
    purchaseOrder: "Sipariş",
    goodsReceipt: "Mal kabul",
    purchaseInvoice: "Alış faturası",
    landedCost: "Masraf dağıtımı",
    attachment: "Ek dosya",
};

export type ImportFileInput = { supplierId: string; originCountry: string; incoterm: Incoterm; currency: string; notes?: string | null };
export type ImportLinkDto = { id: string; kind: ImportLinkKind; refId: string; label: string | null };
export type ImportFileDto = {
    id: string;
    number: string;
    status: ImportFileStatus;
    supplierId: string;
    supplierName: string;
    originCountry: string;
    incoterm: string;
    currency: string;
    notes: string | null;
    links: ImportLinkDto[];
};
export type ImportFileRow = { id: string; number: string; status: ImportFileStatus; supplierId: string; supplierName: string; originCountry: string; incoterm: string; currency: string };
export type ImportSummary = {
    importFileId: string;
    goodsValueTl: number;
    costsByType: { costType: string; amountTl: number }[];
    totalCostTl: number;
    landedTotalTl: number;
    items: { productId: string; productCode: string; productName: string; variantId: string | null; baseQuantity: number; goodsValueTl: number; allocatedCostTl: number; landedUnitCostTl: number }[];
};

export type SuggestionLine = {
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    warehouseId: string;
    available: number;
    onOrder: number;
    minQty: number;
    suggestedQty: number;
    lastPrice: number | null;
};
export type SuggestionGroup = { supplierId: string | null; supplierName: string; lines: SuggestionLine[] };
export type SuggestionOrderInput = { groups: { supplierId: string; lines: { productId: string; variantId?: string | null; warehouseId: string; quantity: number }[] }[] };

function qs(params: Record<string, string | number | boolean | undefined>): string {
    const search = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== "") search.set(k, String(v));
    const text = search.toString();
    return text ? `?${text}` : "";
}

export const purchasing = {
    orders: {
        list: (q: PoQuery = {}) => apiFetch<{ items: PoRow[]; total: number }>(`${BASE}/orders${qs({ ...q })}`),
        get: (id: string) => apiFetch<PoDto>(`${BASE}/orders/${id}`),
        create: (body: PoInput) => apiFetch<PoDto>(`${BASE}/orders`, { method: "POST", body }),
        update: (id: string, body: PoInput) => apiFetch<PoDto>(`${BASE}/orders/${id}`, { method: "PUT", body }),
        approve: (id: string) => apiFetch<PoDto>(`${BASE}/orders/${id}/approve`, { method: "POST" }),
        cancel: (id: string, reason: string) => apiFetch<PoDto>(`${BASE}/orders/${id}/cancel`, { method: "POST", body: { reason } }),
        close: (id: string, reason: string) => apiFetch<PoDto>(`${BASE}/orders/${id}/close`, { method: "POST", body: { reason } }),
        copy: (id: string) => apiFetch<PoDto>(`${BASE}/orders/${id}/copy`, { method: "POST" }),
        pdf: (id: string) => fetchBlob(`${BASE}/orders/${id}/pdf`),
        email: (id: string, body: { to?: string[]; cc?: string[]; subject?: string; body?: string }) => apiFetch<void>(`${BASE}/orders/${id}/email`, { method: "POST", body }),
    },
    receipts: {
        list: (q: ReceiptQuery = {}) => apiFetch<PagedResult<ReceiptRow>>(`${BASE}/receipts${qs({ ...q })}`),
        get: (id: string) => apiFetch<ReceiptDto>(`${BASE}/receipts/${id}`),
        create: (body: ReceiptInput) => apiFetch<ReceiptDto>(`${BASE}/receipts`, { method: "POST", body }),
        cancel: (id: string, reason: string) => apiFetch<ReceiptDto>(`${BASE}/receipts/${id}/cancel`, { method: "POST", body: { reason } }),
        invoiceDraft: (body: { receiptIds: string[]; externalNumber: string; date?: string }) =>
            apiFetch<InvoiceDto>(`${BASE}/receipts/invoice-draft`, { method: "POST", body }),
    },
    quarantine: {
        list: (branchId?: string) => apiFetch<QuarantineRow[]>(`${BASE}/quarantine${qs({ branchId })}`),
        release: (body: { receiptLineId: string; quantity: number; targetWarehouseId: string }) =>
            apiFetch<QuarantineResult>(`${BASE}/quarantine/release`, { method: "POST", body }),
        reject: (body: { receiptLineId: string; quantity: number; action: "damaged" | "return" }) =>
            apiFetch<QuarantineResult>(`${BASE}/quarantine/reject`, { method: "POST", body }),
    },
    landedCosts: {
        list: (q: { status?: LandedCostStatus; importFileId?: string } = {}) => apiFetch<LandedCostRow[]>(`${BASE}/landed-costs${qs({ ...q })}`),
        get: (id: string) => apiFetch<LandedCostDto>(`${BASE}/landed-costs/${id}`),
        preview: (body: LandedCostInput) => apiFetch<LandedCostDto>(`${BASE}/landed-costs/preview`, { method: "POST", body }),
        create: (body: LandedCostInput) => apiFetch<LandedCostDto>(`${BASE}/landed-costs`, { method: "POST", body }),
        post: (id: string) => apiFetch<LandedCostDto>(`${BASE}/landed-costs/${id}/post`, { method: "POST" }),
        cancel: (id: string, reason: string) => apiFetch<LandedCostDto>(`${BASE}/landed-costs/${id}/cancel`, { method: "POST", body: { reason } }),
    },
    importFiles: {
        list: (status?: ImportFileStatus) => apiFetch<ImportFileRow[]>(`${BASE}/import-files${qs({ status })}`),
        get: (id: string) => apiFetch<ImportFileDto>(`${BASE}/import-files/${id}`),
        create: (body: ImportFileInput) => apiFetch<ImportFileDto>(`${BASE}/import-files`, { method: "POST", body }),
        update: (id: string, body: ImportFileInput) => apiFetch<ImportFileDto>(`${BASE}/import-files/${id}`, { method: "PUT", body }),
        link: (id: string, kind: "po" | "receipt" | "invoice" | "landedCost" | "attachment", refId: string) =>
            apiFetch<ImportFileDto>(`${BASE}/import-files/${id}/link`, { method: "POST", body: { kind, id: refId } }),
        close: (id: string) => apiFetch<ImportFileDto>(`${BASE}/import-files/${id}/close`, { method: "POST" }),
        summary: (id: string) => apiFetch<ImportSummary>(`${BASE}/import-files/${id}/summary`),
    },
    suggestions: {
        get: (branchId?: string) => apiFetch<{ groups: SuggestionGroup[] }>(`${BASE}/suggestions${qs({ branchId })}`),
        createOrders: (body: SuggestionOrderInput) => apiFetch<PoDto[]>(`${BASE}/suggestions/create-orders`, { method: "POST", body }),
    },
    createReturn: (body: ReturnInput) => apiFetch<ReturnResult>(`${BASE}/returns`, { method: "POST", body }),
    priceStats: (productId: string, variantId?: string | null) => apiFetch<PriceStat[]>(`${BASE}/price-stats${qs({ productId, variantId: variantId ?? undefined })}`),
};
