import { download } from "./catalog";
import { ApiError, apiFetch } from "./client";
import type { PagedResult } from "./erp";

// ERP servisi (services/erp) stok uç noktalarının tipli sarmalayıcıları (/api/erp/inventory/*).
// Enum'lar camelCase string, tarihler "YYYY-MM-DD", miktarlar temel birimde JSON sayısıdır.

export type WarehouseKind = "normal" | "quarantine" | "damaged";
export type MovementType =
    | "opening"
    | "receipt"
    | "issue"
    | "transferOut"
    | "transferIn"
    | "countAdjustment"
    | "revaluation"
    | "purchaseReceipt"
    | "purchaseReturn"
    | "salesShipment"
    | "salesReturn"
    | "bundleIssue";
export type StockDocumentKind = "receipt" | "issue" | "opening";
export type StockDocumentStatus = "active" | "cancelled";
export type CountStatus = "open" | "posted" | "cancelled";
export type CostingMethod = "weightedAverage" | "fifo";

export type WarehouseDto = {
    id: string;
    branchId: string;
    code: string;
    name: string;
    kind: WarehouseKind;
    isDefault: boolean;
    isActive: boolean;
    inUse: boolean;
};

export type WarehouseInput = { code: string; name: string; kind: WarehouseKind; branchId?: string };

export type BalanceRow = {
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    variantCode: string | null;
    warehouseId: string;
    warehouseName: string;
    unit: string;
    physical: number;
    reserved: number;
    available: number;
    minQty: number | null;
    /** maliyet.read yoksa null. */
    avgCost: number | null;
};

export type BalanceQuery = {
    warehouseId?: string;
    search?: string;
    categoryId?: string;
    onlyNonZero?: boolean;
    belowMinimum?: boolean;
    page?: number;
    pageSize?: number;
};

export type ItemCardMovement = {
    id: string;
    date: string;
    type: MovementType;
    warehouseId: string;
    sourceType: string;
    sourceId: string;
    in: number;
    out: number;
    running: number;
    unitCost: number | null;
    totalCost: number | null;
    note: string | null;
};

export type ItemCard = {
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    unit: string;
    openingQty: number;
    closingQty: number;
    movements: ItemCardMovement[];
};

export type LotDto = { lotId: string; lotNo: string; expiry: string | null; available: number };
export type LotPick = { lotId: string; lotNo: string; expiry: string | null; quantity: number };
export type SerialDto = { id: string; serial: string; warehouseId: string | null; inStock: boolean };

export type LotInput = { lotNo: string; expiry: string | null; quantity: number; lotId?: string | null };

export type DocumentLineInput = {
    productId: string;
    variantId: string | null;
    unit: string;
    quantity: number;
    unitCost?: number | null;
    lots?: LotInput[];
    serials?: string[];
};

export type StockDocumentInput = {
    kind: StockDocumentKind;
    date: string;
    warehouseId: string;
    reason?: string | null;
    lines: DocumentLineInput[];
};

export type TransferInput = {
    date: string;
    fromWarehouseId: string;
    toWarehouseId: string;
    note?: string | null;
    lines: DocumentLineInput[];
};

export type DocumentCreated = { id: string; number: string };

export type StockDocumentRow = {
    id: string;
    number: string;
    kind: StockDocumentKind;
    status: StockDocumentStatus;
    date: string;
    warehouseId: string;
    reason: string | null;
    lineCount: number;
};

export type StockDocumentLine = {
    id: string;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    unit: string;
    quantity: number;
    baseQuantity: number;
    unitCost: number | null;
    trackingJson: string | null;
};

export type StockDocument = Omit<StockDocumentRow, "lineCount"> & {
    cancelReason: string | null;
    createdAt: string;
    createdBy: string;
    lines: StockDocumentLine[];
};

export type DocumentQuery = { kind?: StockDocumentKind; warehouseId?: string; from?: string; to?: string; page?: number; pageSize?: number };

export type TransferRow = { id: string; number: string; date: string; fromWarehouseId: string; toWarehouseId: string; lineCount: number };

export type CountLine = {
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    expectedQty: number;
    countedQty: number | null;
};

export type CountRow = { id: string; number: string; name: string | null; warehouseId: string; status: CountStatus; date: string; lineCount: number };
export type Count = Omit<CountRow, "lineCount"> & { snapshotAt: string; postedAt: string | null; lines: CountLine[] };
export type CountLineInput = { productId: string; variantId: string | null; countedQty: number | null };

export type MinimumRow = {
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    minQty: number;
    reorderQty: number | null;
};
export type MinimumInput = { productId: string; variantId: string | null; minQty: number; reorderQty: number | null };

export type OpeningRowResult = { rowNumber: number; action: string; warehouseCode: string | null; productCode: string | null; errors: string[] };
export type OpeningPreview = { batchId: string; total: number; creates: number; errors: number; rows: OpeningRowResult[] };
export type OpeningCommitResult = { documents: number; lines: number };
export type OpeningCommitOutcome = { kind: "done"; result: OpeningCommitResult } | { kind: "conflict"; preview: OpeningPreview };

export type StockSettings = { method: CostingMethod; allowNegative: boolean };

const base = "/api/erp/inventory";

function queryString(params: Record<string, string | number | boolean | undefined>): string {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value !== undefined && value !== "") {
            search.set(key, String(value));
        }
    }
    const text = search.toString();
    return text ? `?${text}` : "";
}

const get = <T>(path: string) => apiFetch<T>(`${base}${path}`);
const send = <T = void>(method: string, path: string, body?: unknown) => apiFetch<T>(`${base}${path}`, { method, body });

export const inventory = {
    warehouses: () => get<WarehouseDto[]>("/warehouses"),
    defaultWarehouse: (branchId: string) => get<WarehouseDto>(`/warehouses/default${queryString({ branchId })}`),
    createWarehouse: (body: WarehouseInput) => send<WarehouseDto>("POST", "/warehouses", body),
    updateWarehouse: (id: string, body: WarehouseInput) => send<WarehouseDto>("PUT", `/warehouses/${id}`, body),
    setWarehouseActive: (id: string, active: boolean) => send("POST", `/warehouses/${id}/${active ? "activate" : "deactivate"}`),
    deleteWarehouse: (id: string) => send("DELETE", `/warehouses/${id}`),

    balances: (q: BalanceQuery = {}) =>
        get<PagedResult<BalanceRow>>(
            `/balances${queryString({
                warehouseId: q.warehouseId,
                search: q.search,
                categoryId: q.categoryId,
                onlyNonZero: q.onlyNonZero ? true : undefined,
                belowMinimum: q.belowMinimum ? true : undefined,
                page: q.page,
                pageSize: q.pageSize,
            })}`,
        ),
    itemCard: (q: { productId: string; variantId?: string | null; warehouseId?: string; from?: string; to?: string }) =>
        get<ItemCard>(`/item-card${queryString({ productId: q.productId, variantId: q.variantId ?? undefined, warehouseId: q.warehouseId, from: q.from, to: q.to })}`),

    lots: (productId: string, variantId: string | null, warehouseId?: string) =>
        get<LotDto[]>(`/tracking/lots${queryString({ productId, variantId: variantId ?? undefined, warehouseId })}`),
    suggestLots: (productId: string, variantId: string | null, warehouseId: string, qty: number) =>
        get<LotPick[]>(`/tracking/lots/suggest${queryString({ productId, variantId: variantId ?? undefined, warehouseId, qty })}`),
    serials: (productId: string, variantId: string | null, warehouseId: string, inStock = true) =>
        get<SerialDto[]>(`/tracking/serials${queryString({ productId, variantId: variantId ?? undefined, warehouseId, inStock })}`),

    createDocument: (body: StockDocumentInput) => send<DocumentCreated>("POST", "/documents", body),
    documents: (q: DocumentQuery = {}) => get<PagedResult<StockDocumentRow>>(`/documents${queryString({ ...q })}`),
    document: (id: string) => get<StockDocument>(`/documents/${id}`),
    cancelDocument: (id: string, reason: string) => send("POST", `/documents/${id}/cancel`, { reason }),

    createTransfer: (body: TransferInput) => send<DocumentCreated>("POST", "/transfers", body),
    transfers: (page = 1, pageSize = 50) => get<PagedResult<TransferRow>>(`/transfers${queryString({ page, pageSize })}`),

    createCount: (body: { warehouseId: string; categoryId?: string | null; name?: string | null }) => send<DocumentCreated>("POST", "/counts", body),
    counts: (warehouseId?: string) => get<CountRow[]>(`/counts${queryString({ warehouseId })}`),
    count: (id: string) => get<Count>(`/counts/${id}`),
    setCountLines: (id: string, lines: CountLineInput[]) => send("PUT", `/counts/${id}/lines`, { lines }),
    postCount: (id: string) => send("POST", `/counts/${id}/post`),
    cancelCount: (id: string) => send("POST", `/counts/${id}/cancel`),

    minimums: (warehouseId: string) => get<MinimumRow[]>(`/minimums${queryString({ warehouseId })}`),
    setMinimums: (warehouseId: string, rows: MinimumInput[]) => send("PUT", "/minimums", { warehouseId, rows }),

    openingTemplate: () => download(`${base}/opening-import/template.xlsx`, "acilis-stogu-sablonu.xlsx"),
    previewOpening: (file: File) => {
        const form = new FormData();
        form.append("file", file);
        return apiFetch<OpeningPreview>(`${base}/opening-import/preview`, { method: "POST", body: form });
    },
    /** 409 import_conflict hata olarak değil, taze önizlemeyle birlikte sonuç olarak döner. */
    commitOpening: async (batchId: string): Promise<OpeningCommitOutcome> => {
        try {
            return { kind: "done", result: await apiFetch<OpeningCommitResult>(`${base}/opening-import/${batchId}/commit`, { method: "POST" }) };
        } catch (e) {
            const preview = e instanceof ApiError && e.code === "import_conflict" ? e.problem?.preview : undefined;
            if (preview) {
                return { kind: "conflict", preview: preview as OpeningPreview };
            }
            throw e;
        }
    },
};

export const WAREHOUSE_KIND_LABELS: Record<WarehouseKind, string> = { normal: "Normal", quarantine: "Karantina", damaged: "Hasarlı" };

export const MOVEMENT_LABELS: Record<MovementType, string> = {
    opening: "Açılış",
    receipt: "Giriş fişi",
    issue: "Çıkış fişi",
    transferOut: "Transfer çıkış",
    transferIn: "Transfer giriş",
    countAdjustment: "Sayım farkı",
    revaluation: "Maliyet düzeltme",
    purchaseReceipt: "Alış girişi",
    purchaseReturn: "Alış iadesi",
    salesShipment: "Satış sevkiyatı",
    salesReturn: "Satış iadesi",
    bundleIssue: "Set çıkışı",
};

const quantityFormat = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 6 });

/** tr-TR miktar: gereksiz sıfırlar yok, en fazla 6 ondalık. */
export function formatQuantity(value: number): string {
    return quantityFormat.format(value);
}

export type UnitFactor = { unit: string; factor: number };

/**
 * "25 adet (2 koli 1 adet)": temel birimdeki miktarı, çarpanı en büyük olan birimle ifade eder.
 * Yalnızca tam kat varsa ya da çarpan 1'den büyük bir birim tanımlıysa parantez eklenir.
 */
export function formatWithBreakdown(qty: number, baseUnit: string, units: UnitFactor[] = []): string {
    const text = `${formatQuantity(qty)} ${baseUnit}`;
    const largest = units.filter((u) => u.factor > 1 && u.unit !== baseUnit).sort((a, b) => b.factor - a.factor)[0];
    if (!largest || Math.abs(qty) < largest.factor) {
        return text;
    }
    const sign = qty < 0 ? "-" : "";
    const abs = Math.abs(qty);
    const whole = Math.floor(abs / largest.factor + 1e-9);
    const rest = Math.round((abs - whole * largest.factor) * 1e6) / 1e6;
    const parts = [`${whole} ${largest.unit}`];
    if (rest > 0) {
        parts.push(`${formatQuantity(rest)} ${baseUnit}`);
    }
    return `${text} (${sign}${parts.join(" ")})`;
}

/** Kaynak türü → belge bağlantısı; bilinmeyen kaynaklar bağlantısız (null) kalır. */
export function sourceHref(sourceType: string, sourceId: string): string | null {
    switch (sourceType) {
        case "stock-document":
            return `/stok/fisler?belge=${sourceId}`;
        case "transfer":
            return `/stok/fisler?transfer=${sourceId}`;
        case "stock-count":
            return `/stok/sayim/${sourceId}`;
        default:
            return null;
    }
}

export const SOURCE_LABELS: Record<string, string> = { "stock-document": "Stok fişi", transfer: "Transfer", "stock-count": "Sayım" };
