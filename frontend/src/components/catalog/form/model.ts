import type { CatalogOptions, CustomFieldDto, Money4, ProductDto, ProductInput, ProductKind } from "@/lib/api/catalog";
import { parseUnitPrice, type Currency } from "@/lib/money";

export type MoneyValue = { amount: string; currency: Currency; vatIncluded: boolean };
export type UnitRow = { unit: string; factor: string; barcode: string };
export type SupplierRow = { currentAccountId: string; name: string; supplierCode: string; isDefault: boolean };

export type FormValues = {
    kind: ProductKind;
    code: string;
    name: string;
    description: string;
    categoryId: string;
    brandId: string;
    baseUnit: string;
    vatRate: string;
    soldByMeasure: boolean;
    measureUnit: "m2" | "m";
    tracksSerialLot: boolean;
    barcode: string;
    weightKg: string;
    widthCm: string;
    lengthCm: string;
    heightCm: string;
    originCountry: string;
    gtip: string;
    purchase: MoneyValue;
    sale: MoneyValue;
    tags: string[];
    customFields: Record<string, unknown>;
    units: UnitRow[];
    suppliers: SupplierRow[];
    imageFileIds: string[];
};

export type Errors = Partial<Record<string, string>>;

export const MAX_IMAGES = 10;

const num = (value: number | null | undefined) => (value == null ? "" : String(value).replace(".", ","));

const moneyValue = (m: Money4 | null): MoneyValue => ({
    amount: m ? num(m.amount) : "",
    currency: m?.currency ?? "TRY",
    vatIncluded: m?.vatIncluded ?? true,
});

export function initialValues(product: ProductDto | undefined, kind: ProductKind, settings: Pick<CatalogOptions, "units" | "defaultVatRate">): FormValues {
    if (product) {
        return {
            kind: product.kind,
            code: product.code,
            name: product.name,
            description: product.description ?? "",
            categoryId: product.categoryId ?? "",
            brandId: product.brandId ?? "",
            baseUnit: product.baseUnit,
            vatRate: String(product.vatRate),
            soldByMeasure: product.soldByMeasure,
            measureUnit: product.measureUnit === "m" ? "m" : "m2",
            tracksSerialLot: product.tracksSerialLot,
            barcode: product.barcode ?? "",
            weightKg: num(product.weightKg),
            widthCm: num(product.widthCm),
            lengthCm: num(product.lengthCm),
            heightCm: num(product.heightCm),
            originCountry: product.originCountry ?? "",
            gtip: product.gtip ?? "",
            purchase: moneyValue(product.defaultPurchasePrice),
            sale: moneyValue(product.defaultSalePrice),
            tags: product.tags,
            customFields: product.customFields ?? {},
            units: product.units.map((u) => ({ unit: u.unit, factor: num(u.factor), barcode: u.barcode ?? "" })),
            suppliers: product.suppliers.map((s) => ({
                currentAccountId: s.currentAccountId,
                name: s.currentAccountName ?? "",
                supplierCode: s.supplierCode ?? "",
                isDefault: s.isDefault,
            })),
            imageFileIds: [...product.images].sort((a, b) => a.sort - b.sort).map((i) => i.fileId),
        };
    }
    const service = kind === "service";
    const preferred = service ? "saat" : "adet";
    const baseUnit = settings.units.includes(preferred) ? preferred : (settings.units[0] ?? preferred);
    return {
        kind,
        code: "",
        name: "",
        description: "",
        categoryId: "",
        brandId: "",
        baseUnit,
        vatRate: String(settings.defaultVatRate),
        soldByMeasure: false,
        measureUnit: "m2",
        tracksSerialLot: false,
        barcode: "",
        weightKg: "",
        widthCm: "",
        lengthCm: "",
        heightCm: "",
        originCountry: "",
        gtip: "",
        purchase: moneyValue(null),
        sale: moneyValue(null),
        tags: [],
        customFields: {},
        units: [],
        suppliers: [],
        imageFileIds: [],
    };
}

/** Birim çevrim katsayısı: sıfırdan büyük, en çok 6 ondalık (virgül ya da nokta). */
export function parseFactor(text: string): number | null {
    const t = text.trim();
    if (!/^\d+([.,]\d{1,6})?$/.test(t)) return null;
    const n = Number(t.replace(",", "."));
    return n > 0 ? n : null;
}

/** Birim satırlarının hatalarını satır dizinine göre verir. */
export function validateUnits(rows: UnitRow[], baseUnit: string): Record<number, string> {
    const errors: Record<number, string> = {};
    const seen = new Set<string>();
    rows.forEach((row, i) => {
        if (!row.unit) errors[i] = "Birim seçin.";
        else if (row.unit === baseUnit) errors[i] = "Temel birim ayrıca eklenmez.";
        else if (seen.has(row.unit)) errors[i] = "Bu birim zaten eklendi.";
        else if (parseFactor(row.factor) === null) errors[i] = "Katsayı sıfırdan büyük olmalı, en fazla 6 ondalık.";
        seen.add(row.unit);
    });
    return errors;
}

const optionalDecimal = (text: string): number | null => {
    const t = text.trim();
    if (!t) return null;
    return /^\d+([.,]\d{1,4})?$/.test(t) ? Number(t.replace(",", ".")) : Number.NaN;
};

function moneyOf(m: MoneyValue): Money4 | null | "invalid" {
    if (!m.amount.trim()) return null;
    const amount = parseUnitPrice(m.amount);
    return amount === null ? "invalid" : { amount, currency: m.currency, vatIncluded: m.vatIncluded };
}

export function validate(v: FormValues, definitions: CustomFieldDto[]): Errors {
    const errors: Errors = {};
    const code = v.code.trim();
    if (code.length > 30) errors.code = "Kod en fazla 30 karakter olabilir.";
    if (!code) errors.code = "Kod zorunlu.";
    const name = v.name.trim();
    if (name.length < 1 || name.length > 200) errors.name = "Ad 1 ile 200 karakter arasında olmalı.";
    if (v.barcode.trim().length > 40) errors.barcode = "Barkod en fazla 40 karakter olabilir.";
    if (moneyOf(v.purchase) === "invalid") errors.purchaseAmount = "Geçerli bir tutar girin (en çok 4 ondalık). Örn. 149,90";
    if (moneyOf(v.sale) === "invalid") errors.saleAmount = "Geçerli bir tutar girin (en çok 4 ondalık). Örn. 149,90";
    for (const key of ["weightKg", "widthCm", "lengthCm", "heightCm"] as const) {
        if (Number.isNaN(optionalDecimal(v[key]))) errors[key] = "Geçerli bir sayı girin.";
    }
    if (Object.keys(validateUnits(v.units, v.baseUnit)).length > 0) errors.units = "Birimlerdeki hataları düzeltin.";
    for (const def of definitions) {
        const applies = v.kind === "product" ? def.appliesToProduct : def.appliesToService;
        const value = v.customFields[def.key];
        if (applies && def.required && (value === undefined || value === "" || value === null)) {
            errors[`customFields.${def.key}`] = `"${def.label}" alanı zorunlu.`;
        }
    }
    return errors;
}

export function buildInput(v: FormValues, definitions: CustomFieldDto[]): ProductInput {
    const service = v.kind === "service";
    const blank = (t: string) => t.trim() || null;
    const custom: Record<string, unknown> = {};
    for (const def of definitions) {
        const value = v.customFields[def.key];
        if (value !== undefined && value !== "" && value !== null) custom[def.key] = value;
    }
    const measure = !service && v.soldByMeasure;
    const money = (m: MoneyValue) => {
        const r = moneyOf(m);
        return r === "invalid" ? null : r;
    };
    return {
        kind: v.kind,
        code: blank(v.code),
        name: v.name.trim(),
        description: blank(v.description),
        categoryId: v.categoryId || null,
        brandId: v.brandId || null,
        baseUnit: measure ? v.measureUnit : v.baseUnit,
        vatRate: Number(v.vatRate),
        soldByMeasure: measure,
        measureUnit: measure ? v.measureUnit : null,
        tracksSerialLot: !service && v.tracksSerialLot,
        barcode: blank(v.barcode),
        weightKg: optionalDecimal(v.weightKg),
        widthCm: optionalDecimal(v.widthCm),
        lengthCm: optionalDecimal(v.lengthCm),
        heightCm: optionalDecimal(v.heightCm),
        originCountry: blank(v.originCountry),
        gtip: blank(v.gtip),
        defaultPurchasePrice: money(v.purchase),
        defaultSalePrice: money(v.sale),
        tags: v.tags,
        customFields: custom,
        units: v.units.map((u) => ({ unit: u.unit, factor: parseFactor(u.factor) ?? 0, barcode: blank(u.barcode) })),
        suppliers: v.suppliers.map((s) => ({ currentAccountId: s.currentAccountId, supplierCode: blank(s.supplierCode), isDefault: s.isDefault })),
        images: v.imageFileIds.map((fileId) => ({ fileId })),
    };
}
