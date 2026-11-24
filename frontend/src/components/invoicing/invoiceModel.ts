import type { SellableItem } from "@/lib/api/catalog";
import type { LotInput } from "@/lib/api/inventory";
import type { DueRowInput, InvoiceInput, LineDto, LineInput } from "@/lib/api/invoicing";
import { addDaysIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";

// Not: Burada hiçbir vergi/toplam hesabı yapılmaz; toplamlar yalnızca sunucudaki önizleme ucundan gelir.
// Bu dosya yalnızca form taslaklarını API gövdesine çevirir ve vade planını bölüştürür.

export const VAT_RATES = [0, 1, 10, 20] as const;

export type InvoiceLineDraft = {
    key: string;
    productId: string | null;
    variantId: string | null;
    code: string;
    name: string;
    baseUnit: string;
    units: { unit: string; factor: number }[];
    unit: string;
    quantity: string;
    soldByMeasure: boolean;
    measureUnit: string | null;
    width: string;
    length: string;
    unitPrice: string;
    vatIncluded: boolean;
    discountMode: "percent" | "amount";
    discountValue: string;
    vatRate: number;
    withholdingCode: string;
    exemptionCode: string;
    exemptionReason: string;
    tracksSerialLot: boolean;
    trackingMode: "lot" | "serial" | null;
    lots: LotInput[];
    sourceLineId: string | null;
    error?: string | null;
};

let counter = 0;
const nextKey = () => `inv-line-${++counter}`;

export function newInvoiceLine(item: SellableItem): InvoiceLineDraft {
    return {
        key: nextKey(),
        productId: item.productId,
        variantId: item.variantId,
        code: item.code,
        name: item.name,
        baseUnit: item.baseUnit,
        units: item.units.map((u) => ({ unit: u.unit, factor: u.factor })),
        unit: item.baseUnit,
        quantity: "1",
        soldByMeasure: item.soldByMeasure,
        measureUnit: item.measureUnit,
        width: "",
        length: "",
        unitPrice: "",
        vatIncluded: false,
        discountMode: "percent",
        discountValue: "",
        vatRate: item.vatRate,
        withholdingCode: "",
        exemptionCode: "",
        exemptionReason: "",
        tracksSerialLot: item.tracksSerialLot,
        trackingMode: item.tracksSerialLot ? "lot" : null,
        lots: [],
        sourceLineId: null,
        error: null,
    };
}

/** Kayıtlı fatura satırından düzenlenebilir taslak (kalem bilgisi bilinmiyorsa açıklama ve birim yeterlidir). */
export function lineFromDto(dto: LineDto, product?: Partial<Omit<SellableItem, "units">> & { units?: { unit: string; factor: number }[] }): InvoiceLineDraft {
    const byMeasure = dto.width != null || dto.length != null;
    return {
        key: nextKey(),
        productId: dto.productId ?? null,
        variantId: dto.variantId ?? null,
        code: product?.code ?? "",
        name: product?.name ?? dto.description,
        baseUnit: product?.baseUnit ?? dto.unit,
        units: product?.units ?? [],
        unit: dto.unit,
        quantity: String(dto.quantity).replace(".", ","),
        soldByMeasure: product?.soldByMeasure ?? byMeasure,
        measureUnit: product?.measureUnit ?? (dto.width == null && dto.length != null ? "m" : byMeasure ? "m2" : null),
        width: dto.width != null ? String(dto.width).replace(".", ",") : "",
        length: dto.length != null ? String(dto.length).replace(".", ",") : "",
        unitPrice: String(dto.unitPrice).replace(".", ","),
        vatIncluded: dto.vatIncluded,
        discountMode: dto.discountAmount != null ? "amount" : "percent",
        discountValue: String(dto.discountAmount ?? dto.discountPercent ?? "").replace(".", ","),
        vatRate: dto.vatRate,
        withholdingCode: dto.withholdingCode ?? "",
        exemptionCode: dto.exemptionCode ?? "",
        exemptionReason: dto.exemptionReason ?? "",
        tracksSerialLot: product?.tracksSerialLot ?? false,
        trackingMode: product?.tracksSerialLot ? "lot" : null,
        lots: (dto.lots ?? []).map((l) => ({ lotNo: l.lotNo, expiry: l.expiry, quantity: l.quantity, lotId: l.lotId ?? null })),
        sourceLineId: dto.sourceLineId ?? null,
        error: null,
    };
}

const num = (text: string, maxDecimals: 2 | 4 = 4): number | null => parseAmount(text, { maxDecimals });

/** Ölçü kaleminde gösterim amaçlı miktar (en x boy ya da boy); sunucu kendi hesabını yapar. */
export function measureQuantity(line: InvoiceLineDraft): number | null {
    const length = num(line.length);
    if (length === null || length <= 0) return null;
    if (line.measureUnit?.toLowerCase() === "m") return length;
    const width = num(line.width);
    return width !== null && width > 0 ? Math.round(width * length * 1e6) / 1e6 : null;
}

export function lineQty(line: InvoiceLineDraft): number | null {
    if (line.soldByMeasure) return measureQuantity(line);
    const q = num(line.quantity);
    return q !== null && q > 0 ? q : null;
}

/** Satırın istemci doğrulaması: hata iletisi ya da null. */
export function validateInvoiceLine(line: InvoiceLineDraft): string | null {
    if (line.soldByMeasure && measureQuantity(line) === null) return "En ve boy girilmelidir.";
    if (!line.soldByMeasure && lineQty(line) === null) return "Miktar sıfırdan büyük olmalıdır.";
    if (num(line.unitPrice) === null) return "Birim fiyat geçersiz.";
    if (line.discountValue.trim() !== "") {
        const d = num(line.discountValue, 2);
        if (d === null || (line.discountMode === "percent" && d > 100)) return "İskonto geçersiz.";
    }
    if (line.exemptionCode && line.vatRate !== 0) return "İstisna kodu için KDV oranı %0 olmalıdır.";
    if (line.exemptionCode && line.withholdingCode) return "Tevkifat ve istisna birlikte seçilemez.";
    if (line.tracksSerialLot && line.trackingMode === "lot" && line.lots.length > 0) {
        const total = line.lots.reduce((s, l) => s + l.quantity, 0);
        if (Math.abs(total - (lineQty(line) ?? 0)) > 1e-6) return "Lot miktarları satır miktarına eşit olmalıdır.";
    }
    return null;
}

/** Geçerli satırı API gövdesine çevirir; geçersizse null. */
export function toLineInput(line: InvoiceLineDraft, includeLots: boolean): LineInput | null {
    const qty = lineQty(line);
    const price = num(line.unitPrice);
    if (qty === null || price === null) return null;
    const discount = line.discountValue.trim() === "" ? null : num(line.discountValue, 2);
    return {
        productId: line.productId,
        variantId: line.variantId,
        description: line.productId ? undefined : line.name,
        unit: line.unit,
        quantity: qty,
        width: line.soldByMeasure ? num(line.width) : null,
        length: line.soldByMeasure ? num(line.length) : null,
        unitPrice: price,
        vatIncluded: line.vatIncluded,
        discountPercent: line.discountMode === "percent" ? discount : null,
        discountAmount: line.discountMode === "amount" ? discount : null,
        vatRate: line.vatRate,
        withholdingCode: line.withholdingCode || null,
        exemptionCode: line.exemptionCode || null,
        exemptionReason: line.exemptionCode ? line.exemptionReason.trim() || null : null,
        sourceLineId: line.sourceLineId,
        lots: includeLots && line.tracksSerialLot && line.trackingMode === "lot" && line.lots.length > 0 ? line.lots : null,
    };
}

// ---- Vade planı -----------------------------------------------------------------------------

export type DueRow = { key: string; dueDate: string; amount: string };

const toCents = (n: number): number => Math.round(n * 100);
const fromCents = (c: number): string => (c / 100).toFixed(2).replace(".", ",");

/** "YYYY-MM-DD" + ay sayısı; ay sonu taşmasında ayın son gününe iner. */
export function addMonthsIso(iso: string, months: number): string {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
    if (!m) return "";
    const total = Number(m[2]) - 1 + months;
    const year = Number(m[1]) + Math.floor(total / 12);
    const month = ((total % 12) + 12) % 12;
    const last = new Date(year, month + 1, 0).getDate();
    const day = Math.min(Number(m[3]), last);
    return `${year}-${String(month + 1).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

let dueCounter = 0;

/** Toplamı eşit böler (kuruş cinsinden aşağı yuvarlanır); kalan kuruşlar son satıra eklenir. */
export function splitDue(total: number, count: number, firstDate: string): DueRow[] {
    const cents = toCents(total);
    const n = Math.max(1, Math.floor(count));
    const each = Math.floor(cents / n);
    return Array.from({ length: n }, (_, i) => ({
        key: `due-${++dueCounter}`,
        dueDate: addMonthsIso(firstDate, i) || firstDate,
        amount: fromCents(i === n - 1 ? cents - each * (n - 1) : each),
    }));
}

export const dueSum = (rows: DueRow[]): number => rows.reduce((s, r) => s + toCents(parseAmount(r.amount) ?? 0), 0) / 100;

/** Vade satırları toplamı tutarla uyuşmuyorsa ya da satır geçersizse hata iletisi, aksi halde null. */
export function dueScheduleError(rows: DueRow[], total: number): string | null {
    if (rows.some((r) => !r.dueDate || parseAmount(r.amount) === null)) return "Vade satırlarında tarih ve tutar girilmelidir.";
    const sum = toCents(dueSum(rows));
    if (sum !== toCents(total)) return `Vade tutarları toplamı (${fromCents(sum)}) ödenecek tutara (${fromCents(toCents(total))}) eşit olmalıdır.`;
    return null;
}

export const toDueInput = (rows: DueRow[]): DueRowInput[] => rows.map((r) => ({ dueDate: r.dueDate, amount: parseAmount(r.amount) ?? 0 }));

export { addDaysIso };

export type { InvoiceInput };
