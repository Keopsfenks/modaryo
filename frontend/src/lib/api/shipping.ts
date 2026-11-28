import { apiFetch } from "./client";
import { fetchBlob } from "./invoicing";
import type { ShipmentDto } from "./sales";

// ERP sevkiyat uç noktalarının tipli sarmalayıcıları: /api/erp/shipping/* ve /api/erp/sales/shipments/* eylemleri.
// Enum'lar camelCase string; miktarlar ondalık sayı.

const SHIPPING = "/api/erp/shipping";
const SALES = "/api/erp/sales";

export type PickListStatus = "open" | "completed" | "cancelled";
export type PickLot = { lotNo: string; expiry: string | null; quantity: number; lotId: string | null };

export type PickLineDto = {
    id: string;
    sort: number;
    warehouseId: string;
    productId: string;
    variantId: string | null;
    code: string;
    description: string;
    unit: string;
    factor: number;
    quantity: number;
    pickedQuantity: number | null;
    location: string | null;
    suggestedLots: PickLot[] | null;
    pickedLots: PickLot[] | null;
    pickedSerials: string[] | null;
    sources: { shipmentLineId: string; quantity: number }[] | null;
};

export type PickListDto = {
    id: string;
    number: string;
    status: PickListStatus;
    branchId: string;
    warehouseId: string;
    warehouseName: string;
    shipments: { id: string; number: string; orderNumber: string; customerName: string }[];
    version: number;
    lines: PickLineDto[];
};

export type PickListRow = { id: string; number: string; status: PickListStatus; shipmentCount: number; lineCount: number; created: string };

export type ParcelInput = {
    weightKg: number;
    widthCm: number;
    lengthCm: number;
    heightCm: number;
    contents: { shipmentLineId: string; qty: number }[];
};

export type DispatchInput = { carrierCode: string; trackingNo?: string | null; dispatchAt?: string | null; vehicle?: string | null; driver?: string | null };
export type DeliverInput = { receivedBy: string; deliveredAt?: string | null; proofFileId?: string | null; note?: string | null };

export type BulkShipResult = {
    results: { orderId: string; shipmentId: string | null; shipmentNumber: string | null; error: { code: string; message: string } | null }[];
    created: number;
    failed: number;
};

export type CarrierInfo = { code: string; name: string; trackingUrlTemplate: string | null; isActive: boolean };
export type ShippingSettings = {
    usePickPack: boolean;
    notifyCustomerOnShip: boolean;
    carriers: CarrierInfo[];
    shipEmailSubject: string;
    shipEmailBody: string;
};

export const OWN_VEHICLE = "kendi-arac";

/** Ayarlara erişimi olmayan kullanıcılar için kargo listesi (sunucu tohumuyla aynı kodlar; takip şablonu yok). */
export const DEFAULT_CARRIERS: CarrierInfo[] = [
    ["yurtici", "Yurtiçi"],
    ["aras", "Aras"],
    ["mng", "MNG"],
    ["ptt", "PTT Kargo"],
    ["surat", "Sürat"],
    ["hepsijet", "HepsiJet"],
    ["kolaygelsin", "Kolay Gelsin"],
    ["diger", "Diğer"],
    [OWN_VEHICLE, "Kendi aracımız"],
].map(([code, name]) => ({ code, name, trackingUrlTemplate: null, isActive: true }));

export type PlanStatus = "planned" | "inProgress" | "completed" | "cancelled";
export type StopStatus = "pending" | "delivered" | "failed";
export const PLAN_STATUS_LABELS: Record<PlanStatus, string> = { planned: "Planlandı", inProgress: "Yolda", completed: "Tamamlandı", cancelled: "İptal" };
export const STOP_STATUS_LABELS: Record<StopStatus, string> = { pending: "Bekliyor", delivered: "Teslim edildi", failed: "Teslim edilemedi" };

export type PlanStopDto = {
    id: string;
    sort: number;
    shipmentId: string;
    shipmentNumber: string;
    customerName: string;
    customerPhone: string | null;
    address: { title: string; address: string | null; city: string | null; district: string | null } | null;
    status: StopStatus;
    note: string | null;
};

export type PlanDto = {
    id: string;
    number: string;
    branchId: string;
    date: string;
    vehicle: string | null;
    driverUserId: string | null;
    driverName: string | null;
    status: PlanStatus;
    version: number;
    stops: PlanStopDto[];
};

export type PlanRow = { id: string; number: string; date: string; vehicle: string | null; driverName: string | null; status: PlanStatus; stopCount: number; doneCount: number };
export type PlanInput = { date: string; vehicle?: string | null; driverUserId?: string | null; driverName?: string | null; shipmentIds: string[] };

function qs(params: Record<string, string | undefined>): string {
    const search = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v) search.set(k, v);
    const text = search.toString();
    return text ? `?${text}` : "";
}

/** Desi = en x boy x yükseklik / 3000, 0,1 basamağa yukarı yuvarlanır (sunucuyla aynı kural). */
export function computeDesi(widthCm: number, lengthCm: number, heightCm: number): number {
    const raw = (widthCm * lengthCm * heightCm) / 3000;
    return Math.ceil(Math.round(raw * 1e6) / 1e5) / 10;
}

/** Takip bağlantısı: şablondaki {no} takip numarasıyla (URL kodlu) değişir; şablon ya da numara yoksa null. */
export function buildTrackingUrl(template: string | null | undefined, trackingNo: string | null | undefined): string | null {
    if (!template || !trackingNo || !template.includes("{no}")) return null;
    return template.replaceAll("{no}", encodeURIComponent(trackingNo));
}

const post = <T>(path: string, body?: unknown) => apiFetch<T>(path, { method: "POST", body });

export const shipping = {
    pickLists: {
        list: (status?: PickListStatus) => apiFetch<PickListRow[]>(`${SHIPPING}/pick-lists${qs({ status })}`),
        batch: (shipmentIds: string[]) => post<PickListDto>(`${SHIPPING}/pick-lists/batch`, { shipmentIds }),
        get: (id: string) => apiFetch<PickListDto>(`${SHIPPING}/pick-lists/${id}`),
        updateLine: (id: string, lineId: string, body: { pickedQty: number; lots?: PickLot[] | null; serials?: string[] | null }) =>
            apiFetch<PickListDto>(`${SHIPPING}/pick-lists/${id}/lines/${lineId}`, { method: "PUT", body }),
        complete: (id: string, force = false) => post<PickListDto>(`${SHIPPING}/pick-lists/${id}/complete`, { force }),
        cancel: (id: string) => post<PickListDto>(`${SHIPPING}/pick-lists/${id}/cancel`),
        pdf: (id: string) => fetchBlob(`${SHIPPING}/pick-lists/${id}/pdf`),
    },
    shipments: {
        bulk: (orderIds: string[], warehouseId: string, date?: string) => post<BulkShipResult>(`${SALES}/shipments/bulk`, { orderIds, warehouseId, date: date ?? null }),
        pickList: (id: string) => post<PickListDto>(`${SALES}/shipments/${id}/pick-list`),
        parcels: (id: string, parcels: ParcelInput[]) => apiFetch<ShipmentDto>(`${SALES}/shipments/${id}/parcels`, { method: "PUT", body: { parcels } }),
        ship: (id: string, body: DispatchInput) => post<ShipmentDto>(`${SALES}/shipments/${id}/ship`, body),
        deliver: (id: string, body: DeliverInput) => post<ShipmentDto>(`${SALES}/shipments/${id}/deliver`, body),
        fail: (id: string, reason: string) => post<ShipmentDto>(`${SALES}/shipments/${id}/fail`, { reason }),
        resend: (id: string, body: { carrierCode?: string | null; trackingNo?: string | null } = {}) => post<ShipmentDto>(`${SALES}/shipments/${id}/resend`, body),
        returnShipment: (id: string, reason: string) => post<ShipmentDto>(`${SALES}/shipments/${id}/return`, { reason }),
        irsaliyePdf: (id: string) => fetchBlob(`${SALES}/shipments/${id}/irsaliye.pdf`),
    },
    plans: {
        list: (q: { date?: string; status?: PlanStatus } = {}) => apiFetch<PlanRow[]>(`${SHIPPING}/plans${qs(q)}`),
        get: (id: string) => apiFetch<PlanDto>(`${SHIPPING}/plans/${id}`),
        create: (body: PlanInput) => post<PlanDto>(`${SHIPPING}/plans`, body),
        reorder: (id: string, stopIds: string[]) => apiFetch<PlanDto>(`${SHIPPING}/plans/${id}/stops`, { method: "PUT", body: { stopIds } }),
        deliverStop: (id: string, stopId: string, body: DeliverInput) => post<PlanDto>(`${SHIPPING}/plans/${id}/stops/${stopId}/deliver`, body),
        failStop: (id: string, stopId: string, reason: string) => post<PlanDto>(`${SHIPPING}/plans/${id}/stops/${stopId}/fail`, { reason }),
        pdf: (id: string) => fetchBlob(`${SHIPPING}/plans/${id}/pdf`),
    },
};
