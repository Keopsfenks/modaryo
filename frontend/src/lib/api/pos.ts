import { apiFetch } from "./client";
import type { SellableItem } from "./catalog";

// ERP POS uç noktalarının tipli sarmalayıcıları (/api/erp/pos/*). Enum'lar camelCase string; tutarlar JSON sayısı.

const BASE = "/api/erp/pos";

export type LegalDocumentMode = "fiscalReceipt" | "eArchiveInvoice";
export type ScaleValueKind = "weightGrams" | "priceKurus";
export type ScaleBarcodeSetting = { prefix: string; itemCodeLength: number; valueLength: number; valueKind: ScaleValueKind };

export type PosSettings = {
    legalDocumentMode: LegalDocumentMode;
    retailCustomerId: string | null;
    scaleBarcodes: ScaleBarcodeSetting[];
    allowNegativeCashWarningOnly: boolean;
    requireCustomerForVeresiye: boolean;
};

export const MODE_LABELS: Record<LegalDocumentMode, string> = {
    fiscalReceipt: "Yazar kasam var (bilgi fişi)",
    eArchiveInvoice: "Yazar kasam yok (e-Arşiv fatura)",
};

export const SCALE_KIND_LABELS: Record<ScaleValueKind, string> = { weightGrams: "Ağırlık (gram)", priceKurus: "Tutar (kuruş)" };

export type QuickButtonDto = { productId: string; variantId: string | null; code: string | null; name: string | null; presetQty: number | null; color: string };
export type QuickButtonInput = { productId: string; variantId: string | null; presetQty: number | null; color: string };

export type RegisterRow = { id: string; code: string; name: string; branchId: string; warehouseId: string; isActive: boolean };
export type RegisterDto = RegisterRow & {
    cashAccountId: string;
    cardAccountId: string;
    version: number;
    pages: (QuickButtonDto | null)[][];
};
export type RegisterInput = { code: string; name: string; branchId: string; warehouseId: string; cashAccountId: string; cardAccountId: string; isActive?: boolean; version?: number };

export const BUTTON_COLS = 6;
export const BUTTONS_PER_PAGE = 36;
export const MAX_PAGES = 10;
export const DEFAULT_BUTTON_COLOR = "#1976d2";

export type OtherStore = { branchId: string; branchName: string; unit: string; available: number };
export type CampaignResult = { campaignId: string; name: string; discountAmount: number };
export type PosLookup = {
    item: SellableItem;
    unit: string;
    factor: number;
    quantity: number;
    unitPrice: number;
    vatIncluded: boolean;
    fixedTotal: number | null;
    campaign: CampaignResult | null;
    otherStores: OtherStore[];
};

export type PaymentMethod = "cash" | "card" | "onAccount";
export type SaleLineInput = {
    productId: string;
    variantId?: string | null;
    unit?: string | null;
    quantity: number;
    unitPrice?: number | null;
    manualDiscountPercent?: number | null;
    fixedTotal?: number | null;
};
export type SalePaymentInput = { method: PaymentMethod; amount: number };
export type SaleInput = { clientSaleId: string; registerId: string; customerId?: string | null; lines: SaleLineInput[]; payments: SalePaymentInput[] };

export type SlipLine = { description: string; quantity: number; unit: string; unitPrice: number; discount: number; campaignDiscount: number; vatRate: number; total: number };
export type SlipVat = { rate: number; base: number; vat: number };
export type SlipPayment = { method: PaymentMethod | "returnCredit"; amount: number };
export type SlipDto = {
    firm: string;
    branch: string;
    register: string;
    cashier: string;
    number: string;
    at: string;
    mode: LegalDocumentMode;
    lines: SlipLine[];
    vatSummary: SlipVat[];
    subtotal: number;
    discount: number;
    total: number;
    payments: SlipPayment[];
    change: number;
    invoiceNumber: string | null;
    barcode: string;
    notes: string[];
};
export type SaleResult = { id: string; number: string; total: number; change: number; invoiceNumber: string | null; replayed: boolean; slip: SlipDto };

export type SaleLineDto = {
    id: string;
    sort: number;
    productId: string;
    variantId: string | null;
    description: string;
    unit: string;
    quantity: number;
    unitPrice: number;
    vatRate: number;
    discount: number;
    campaignDiscount: number;
    net: number;
    vat: number;
    total: number;
    returnedQuantity: number;
};
export type SaleDto = {
    id: string;
    number: string;
    status: "completed" | "cancelled";
    registerId: string;
    shiftId: string;
    currentAccountId: string;
    date: string;
    at: string;
    mode: LegalDocumentMode;
    subtotal: number;
    discount: number;
    vatTotal: number;
    total: number;
    change: number;
    invoiceId: string | null;
    invoiceNumber: string | null;
    cancelReason: string | null;
    lines: SaleLineDto[];
    payments: SlipPayment[];
    slip: SlipDto;
};

export type ShiftDto = {
    id: string;
    registerId: string;
    registerCode: string;
    cashierUserId: string;
    status: "open" | "closed";
    openedAt: string;
    openingCash: number;
    closedAt: string | null;
    countedCash: number | null;
    expectedCash: number | null;
    difference: number | null;
    closeNote: string | null;
    version: number;
};
export type VatLine = { rate: number; base: number; vat: number };
export type CashMoveDto = { id: string; kind: "in" | "out"; amount: number; reason: string | null; at: string; createdByUserId: string | null };
export type ShiftTotals = {
    salesCount: number;
    salesTotal: number;
    cashSalesNet: number;
    cardSales: number;
    onAccountSales: number;
    returnCreditSales: number;
    changeGiven: number;
    cancelledCount: number;
    cancelledTotal: number;
    returnsCount: number;
    returnsTotal: number;
    cashRefunds: number;
    cardRefunds: number;
    cashIn: number;
    cashOut: number;
    campaignDiscount: number;
    expectedCash: number;
};
export type ShiftReport = { shift: ShiftDto; totals: ShiftTotals; vatSummary: VatLine[]; firstReceipt: string | null; lastReceipt: string | null; cashMoves: CashMoveDto[] };
export type CashMoveResult = { move: CashMoveDto; expectedCash: number; warnings: string[] };

export type ReturnInput = {
    clientReturnId?: string | null;
    saleNumber: string;
    registerId?: string | null;
    lines: { saleLineId: string; quantity: number }[];
    refund?: { method: "cash" | "card"; amount: number }[];
    exchangeLines?: SaleLineInput[];
    exchangePayments?: SalePaymentInput[];
    reason?: string | null;
};
export type ReturnResult = {
    id: string;
    number: string;
    returnValue: number;
    cashRefund: number;
    cardRefund: number;
    onAccountCredit: number;
    exchangeCredit: number;
    difference: number;
    returnInvoiceId: string | null;
    replayed: boolean;
    warnings: string[];
    exchangeSale: SaleResult | null;
};

export type CashierSales = { cashierUserId: string; cashierName: string | null; salesCount: number; salesTotal: number; cancelledCount: number; returnsCount: number; returnsTotal: number };
export type DayReport = {
    registerId: string;
    date: string;
    salesCount: number;
    salesTotal: number;
    cashNet: number;
    card: number;
    onAccount: number;
    returnCredit: number;
    changeGiven: number;
    cancelledCount: number;
    cancelledTotal: number;
    returnsCount: number;
    returnsTotal: number;
    cashRefunds: number;
    cardRefunds: number;
    campaignDiscount: number;
    netSales: number;
    vatSummary: VatLine[];
    returnVatSummary: VatLine[];
    cashiers: CashierSales[];
    firstReceipt: string | null;
    lastReceipt: string | null;
};

const q = (params: Record<string, string | number | undefined>) => {
    const s = new URLSearchParams();
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== "") s.set(k, String(v));
    const t = s.toString();
    return t ? `?${t}` : "";
};
const get = <T>(path: string) => apiFetch<T>(`${BASE}${path}`);
const send = <T = void>(method: string, path: string, body?: unknown) => apiFetch<T>(`${BASE}${path}`, { method, body });

export const pos = {
    registers: () => get<RegisterRow[]>("/registers"),
    register: (id: string) => get<RegisterDto>(`/registers/${id}`),
    createRegister: (body: RegisterInput) => send<RegisterDto>("POST", "/registers", body),
    updateRegister: (id: string, body: RegisterInput) => send<RegisterDto>("PUT", `/registers/${id}`, body),
    setButtons: (id: string, pages: (QuickButtonInput | null)[][]) => send<RegisterDto>("PUT", `/registers/${id}/buttons`, { pages }),
    lookup: (code: string, registerId?: string) => get<PosLookup>(`/lookup${q({ code, registerId })}`),
    search: (text: string) => get<SellableItem[]>(`/lookup/search${q({ q: text })}`),
    createSale: (body: SaleInput) => send<SaleResult>("POST", "/sales", body),
    sale: (id: string) => get<SaleDto>(`/sales/${id}`),
    saleByNumber: (number: string) => get<SaleDto>(`/sales/by-number/${encodeURIComponent(number)}`),
    cancelSale: (id: string, reason: string) => send<SaleDto>("POST", `/sales/${id}/cancel`, { reason }),
    openShift: (registerId: string, openingCash: number) => send<ShiftDto>("POST", "/shifts/open", { registerId, openingCash }),
    closeShift: (id: string, countedCash: number, note?: string | null) => send<ShiftReport>("POST", `/shifts/${id}/close`, { countedCash, note: note ?? null }),
    cashMove: (id: string, kind: "in" | "out", amount: number, reason: string) => send<CashMoveResult>("POST", `/shifts/${id}/cash-moves`, { kind, amount, reason }),
    currentShift: (registerId: string) => get<ShiftDto>(`/shifts/current${q({ registerId })}`),
    shiftReport: (id: string) => get<ShiftReport>(`/reports/shift/${id}`),
    createReturn: (body: ReturnInput) => send<ReturnResult>("POST", "/returns", body),
    dayReport: (registerId: string, date: string) => get<DayReport>(`/reports/day${q({ registerId, date })}`),
    cashierReport: (from: string, to: string, registerId?: string) => get<CashierSales[]>(`/reports/cashiers${q({ from, to, registerId })}`),
};

/** Yeni satış denemesi için istemci kimliği: aynı satışın yeniden gönderimlerinde korunur (sunucuda tekrar yazılmaz). */
export const newClientId = (): string => crypto.randomUUID();

export type ScaleParse =
    | { ok: true; prefix: string; itemCode: string; weightKg: number | null; priceTry: number | null }
    | { ok: false; reason: "length" | "noFormat" | "checkDigit" };

/** EAN-13 kontrol hanesi doğrulaması (sunucudaki kuralla aynı). */
export function ean13Valid(code: string): boolean {
    if (!/^\d{13}$/.test(code)) return false;
    let sum = 0;
    for (let i = 0; i < 12; i++) sum += Number(code[i]) * (i % 2 === 0 ? 1 : 3);
    return (10 - (sum % 10)) % 10 === Number(code[12]);
}

/** Terazi barkodunu çözer: önek eşleşirse kontrol hanesi doğrulanır. Kalem çözümü sunucuda yapılır; bu yalnızca biçim denemesidir. */
export function parseScaleBarcode(code: string, formats: ScaleBarcodeSetting[]): ScaleParse {
    const text = code.trim();
    if (!/^\d{13}$/.test(text)) return { ok: false, reason: "length" };
    const f = formats.find((x) => x.prefix.length > 0 && x.prefix.length + x.itemCodeLength + x.valueLength + 1 === 13 && text.startsWith(x.prefix));
    if (!f) return { ok: false, reason: "noFormat" };
    if (!ean13Valid(text)) return { ok: false, reason: "checkDigit" };
    const itemCode = text.slice(f.prefix.length, f.prefix.length + f.itemCodeLength);
    const raw = Number(text.slice(f.prefix.length + f.itemCodeLength, f.prefix.length + f.itemCodeLength + f.valueLength));
    return f.valueKind === "weightGrams"
        ? { ok: true, prefix: f.prefix, itemCode, weightKg: raw / 1000, priceTry: null }
        : { ok: true, prefix: f.prefix, itemCode, weightKg: null, priceTry: raw / 100 };
}
