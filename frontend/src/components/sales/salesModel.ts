import { catalog, type SellableItem } from "@/lib/api/catalog";
import type { InvoiceInput } from "@/lib/api/invoicing";
import type { OrderLineDto, SalesLineInput, SupplyMode } from "@/lib/api/sales";
import { parseAmount } from "@/lib/money";
import { addMonthsIso } from "@/components/invoicing/invoiceModel";

// Not: Burada vergi/toplam hesabı yapılmaz; toplamlar yalnızca sunucudaki önizleme ucundan gelir.
// Bu dosya form taslaklarını API gövdesine çevirir ve taksit planını kuruş bazında böler.

export const VAT_RATES = [0, 1, 10, 20] as const;

export type SalesLineDraft = {
    key: string;
    productId: string;
    variantId: string | null;
    code: string;
    name: string;
    baseUnit: string;
    units: string[];
    unit: string;
    quantity: string;
    soldByMeasure: boolean;
    measureUnit: string | null;
    width: string;
    length: string;
    unitPrice: string;
    vatIncluded: boolean;
    discountPercent: string;
    vatRate: number;
    supplyMode: SupplyMode;
    /** Kayıtlı siparişten gelen tahmini maliyet/marj (maliyet.read ile). */
    marginPercent: number | null;
    estimatedUnitCost: number | null;
    error?: string | null;
};

let counter = 0;
const nextKey = () => `sales-line-${++counter}`;
const text = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n).replace(".", ","));

export function newSalesLine(item: SellableItem): SalesLineDraft {
    return {
        key: nextKey(),
        productId: item.productId,
        variantId: item.variantId,
        code: item.code,
        name: item.name,
        baseUnit: item.baseUnit,
        units: [item.baseUnit, ...item.units.map((u) => u.unit).filter((u) => u !== item.baseUnit)],
        unit: item.baseUnit,
        quantity: "1",
        soldByMeasure: item.soldByMeasure,
        measureUnit: item.measureUnit,
        width: "",
        length: "",
        unitPrice: "",
        vatIncluded: false,
        discountPercent: "",
        vatRate: item.vatRate,
        supplyMode: "stock",
        marginPercent: null,
        estimatedUnitCost: null,
        error: null,
    };
}

type LineLike = Pick<OrderLineDto, "productId" | "variantId" | "productCode" | "productName" | "unit" | "quantity" | "width" | "length" | "unitPrice" | "vatIncluded" | "discountPercent" | "vatRate" | "supplyMode">;

/** Kayıtlı teklif/sipariş satırından düzenlenebilir taslak. Ölçü kalemi en/boydan anlaşılır. */
export function lineFromDto(l: LineLike & Partial<Pick<OrderLineDto, "marginPercent" | "estimatedUnitCost">>, units?: string[]): SalesLineDraft {
    const byMeasure = l.width != null || l.length != null;
    return {
        key: nextKey(),
        productId: l.productId,
        variantId: l.variantId,
        code: l.productCode,
        name: l.productName,
        baseUnit: units?.[0] ?? l.unit,
        units: units ?? [l.unit],
        unit: l.unit,
        quantity: text(l.quantity),
        soldByMeasure: byMeasure,
        measureUnit: l.width == null && l.length != null ? "m" : byMeasure ? "m2" : null,
        width: text(l.width),
        length: text(l.length),
        unitPrice: text(l.unitPrice),
        vatIncluded: l.vatIncluded,
        discountPercent: text(l.discountPercent),
        vatRate: l.vatRate,
        supplyMode: l.supplyMode,
        marginPercent: l.marginPercent ?? null,
        estimatedUnitCost: l.estimatedUnitCost ?? null,
        error: null,
    };
}

const num = (value: string, maxDecimals: 2 | 4 = 4): number | null => parseAmount(value, { maxDecimals });

/** Ölçü kaleminde gösterim amaçlı miktar (en x boy ya da boy); sunucu kendi hesabını yapar. */
export function measureQty(l: SalesLineDraft): number | null {
    const length = num(l.length);
    if (length === null || length <= 0) return null;
    if (l.measureUnit?.toLowerCase() === "m") return length;
    const width = num(l.width);
    return width !== null && width > 0 ? Math.round(width * length * 1e6) / 1e6 : null;
}

export function lineQty(l: SalesLineDraft): number | null {
    if (l.soldByMeasure) return measureQty(l);
    const q = num(l.quantity);
    return q !== null && q > 0 ? q : null;
}

export function validateSalesLine(l: SalesLineDraft): string | null {
    if (l.soldByMeasure && measureQty(l) === null) return "En ve boy girilmelidir.";
    if (!l.soldByMeasure && lineQty(l) === null) return "Miktar sıfırdan büyük olmalıdır.";
    if (num(l.unitPrice) === null) return "Birim fiyat girilmelidir.";
    if (l.discountPercent.trim()) {
        const d = num(l.discountPercent, 2);
        if (d === null || d > 100) return "İskonto yüzdesi 0-100 arasında olmalıdır.";
    }
    return null;
}

export function toSalesLineInput(l: SalesLineDraft, moduleOn: boolean): SalesLineInput {
    return {
        productId: l.productId,
        variantId: l.variantId,
        unit: l.unit,
        quantity: lineQty(l) ?? 0,
        width: l.soldByMeasure ? num(l.width) : null,
        length: l.soldByMeasure ? num(l.length) : null,
        unitPrice: num(l.unitPrice),
        vatIncluded: l.vatIncluded,
        discountPercent: l.discountPercent.trim() ? num(l.discountPercent, 2) : null,
        vatRate: l.vatRate,
        supplyMode: moduleOn ? l.supplyMode : "stock",
    };
}

/** Sunucu önizlemesi için satış faturası gövdesi: teklif/sipariş aynı hesaplayıcıyı kullanır. */
export function toPreviewInput(args: {
    lines: SalesLineDraft[];
    currentAccountId: string;
    branchId: string;
    date: string;
    currency: string;
    docDiscount: string;
}): InvoiceInput | null {
    if (!args.currentAccountId || !args.branchId || args.lines.length === 0) return null;
    if (args.lines.some((l) => validateSalesLine(l) !== null)) return null;
    const doc = args.docDiscount.trim() ? num(args.docDiscount, 2) : null;
    if (args.docDiscount.trim() && (doc === null || doc > 100)) return null;
    return {
        kind: "sales",
        branchId: args.branchId,
        currentAccountId: args.currentAccountId,
        date: args.date,
        currency: args.currency,
        docDiscountPercent: doc,
        affectsStock: false,
        lines: args.lines.map((l) => ({
            productId: l.productId,
            variantId: l.variantId,
            unit: l.unit,
            quantity: lineQty(l) ?? 0,
            width: l.soldByMeasure ? num(l.width) : null,
            length: l.soldByMeasure ? num(l.length) : null,
            unitPrice: num(l.unitPrice) ?? 0,
            vatIncluded: l.vatIncluded,
            discountPercent: l.discountPercent.trim() ? num(l.discountPercent, 2) : null,
            vatRate: l.vatRate,
        })),
    };
}

// ---- Taksit planı ---------------------------------------------------------------------------

export type PlanRowDraft = { key: string; dueDate: string; amount: string };

const toCents = (n: number): number => Math.round(n * 100);
export const fromCents = (c: number): string => (c / 100).toFixed(2).replace(".", ",");
let planCounter = 0;

/**
 * Peşinattan sonra kalanı eşit böler (kuruş cinsinden); kalan kuruş son satıra eklenir.
 * Halı örneği: 7.360 - 2.453,33 = 4.906,67 -> 2.453,33 + 2.453,34.
 */
export function splitPlan(total: number, downPayment: number, count: number, firstDueDate: string): PlanRowDraft[] {
    const remaining = toCents(total) - toCents(downPayment);
    const n = Math.max(1, Math.floor(count));
    const each = Math.floor(remaining / n);
    return Array.from({ length: n }, (_, i) => ({
        key: `plan-${++planCounter}`,
        dueDate: addMonthsIso(firstDueDate, i) || firstDueDate,
        amount: fromCents(i === n - 1 ? remaining - each * (n - 1) : each),
    }));
}

export const planSumCents = (down: number, rows: PlanRowDraft[]): number => toCents(down) + rows.reduce((s, r) => s + toCents(parseAmount(r.amount) ?? 0), 0);

export function planError(total: number, down: number | null, rows: PlanRowDraft[]): string | null {
    if (down === null || down < 0) return "Peşinat geçersiz.";
    if (rows.length === 0 || rows.length > 36) return "Taksit sayısı 1 ile 36 arasında olmalıdır.";
    if (rows.some((r) => !r.dueDate || parseAmount(r.amount) === null)) return "Taksit satırlarında tarih ve tutar girilmelidir.";
    const sum = planSumCents(down, rows);
    if (sum !== toCents(total)) return `Peşinat ve taksitler toplamı (${fromCents(sum)}) sipariş tutarına (${fromCents(toCents(total))}) eşit olmalıdır.`;
    return null;
}

/** Kayıtlı taslakta birim listesi ve ölçü bilgisi katalogdan tamamlanır (katalog okunamazsa satır olduğu gibi kalır). */
export async function hydrateLines(lines: SalesLineDraft[]): Promise<SalesLineDraft[]> {
    const ids = [...new Set(lines.map((l) => l.productId))];
    const entries = await Promise.all(
        ids.map((id) =>
            catalog.getProduct(id).then(
                (p) => [id, p] as const,
                () => null,
            ),
        ),
    );
    const map = new Map(entries.filter((e): e is NonNullable<typeof e> => e !== null));
    return lines.map((l) => {
        const p = map.get(l.productId);
        if (!p) return l;
        return {
            ...l,
            baseUnit: p.baseUnit,
            units: [...new Set([p.baseUnit, ...p.units.map((u) => u.unit), l.unit])],
            soldByMeasure: p.soldByMeasure || l.soldByMeasure,
            measureUnit: p.soldByMeasure ? (p.measureUnit ?? l.measureUnit) : l.measureUnit,
        };
    });
}
