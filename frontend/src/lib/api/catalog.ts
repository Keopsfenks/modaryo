import { ApiError, apiFetch, type ProblemBody } from "./client";
import type { Currency, PagedResult } from "./erp";

// ERP servisi (services/erp) katalog uç noktalarının tipli sarmalayıcıları (/api/erp/catalog/*).
// Alan adları backend DTO'larının camelCase karşılığıdır.

export type ProductKind = "product" | "service";

export type Money4 = { amount: number; currency: Currency; vatIncluded: boolean };

export type CategoryNode = {
    id: string;
    name: string;
    parentId: string | null;
    sort: number;
    /** Kategoriye doğrudan bağlı ürün sayısı. */
    productCount: number;
    children: CategoryNode[];
};

export type BrandDto = { id: string; name: string; productCount: number };

export type ProductListItem = {
    id: string;
    kind: ProductKind;
    code: string;
    name: string;
    categoryId: string | null;
    categoryPath: string | null;
    brandId: string | null;
    brandName: string | null;
    baseUnit: string;
    vatRate: number;
    isActive: boolean;
    barcode: string | null;
    defaultSalePrice: Money4 | null;
    variantCount: number;
    tags: string[];
    firstImageFileId: string | null;
};

export type ListProductsQuery = {
    search?: string;
    kind?: ProductKind;
    categoryId?: string;
    brandId?: string;
    tag?: string;
    includeInactive?: boolean;
    page?: number;
    pageSize?: number;
};

export type VariantAttributeDto = { id: string; name: string; values: string[]; sort: number };

export type VariantDto = {
    id: string;
    code: string;
    barcode: string | null;
    values: Record<string, string>;
    salePriceOverride: Money4 | null;
    purchasePriceOverride: Money4 | null;
    isActive: boolean;
    inUse: boolean;
};

export type UnitDto = { id: string; unit: string; factor: number; barcode: string | null };
export type SupplierCodeDto = { id: string; currentAccountId: string; currentAccountName: string | null; supplierCode: string | null; isDefault: boolean };
export type ImageDto = { id: string; fileId: string; sort: number };

export type ProductDto = {
    id: string;
    kind: ProductKind;
    code: string;
    name: string;
    description: string | null;
    categoryId: string | null;
    categoryPath: string | null;
    brandId: string | null;
    brandName: string | null;
    baseUnit: string;
    vatRate: number;
    isActive: boolean;
    soldByMeasure: boolean;
    measureUnit: string | null;
    tracksSerialLot: boolean;
    barcode: string | null;
    weightKg: number | null;
    widthCm: number | null;
    lengthCm: number | null;
    heightCm: number | null;
    originCountry: string | null;
    gtip: string | null;
    defaultPurchasePrice: Money4 | null;
    defaultSalePrice: Money4 | null;
    tags: string[];
    customFields: Record<string, unknown>;
    attributes: VariantAttributeDto[];
    variants: VariantDto[];
    units: UnitDto[];
    suppliers: SupplierCodeDto[];
    images: ImageDto[];
    inUse: boolean;
};

/** Oluşturma ve güncelleme gövdesi (varyantlar ayrı uçlarla yönetilir). */
export type ProductInput = {
    kind: ProductKind;
    code: string | null;
    name: string;
    description: string | null;
    categoryId: string | null;
    brandId: string | null;
    baseUnit: string;
    vatRate: number;
    soldByMeasure: boolean;
    measureUnit: string | null;
    tracksSerialLot: boolean;
    barcode: string | null;
    weightKg: number | null;
    widthCm: number | null;
    lengthCm: number | null;
    heightCm: number | null;
    originCountry: string | null;
    gtip: string | null;
    defaultPurchasePrice: Money4 | null;
    defaultSalePrice: Money4 | null;
    tags: string[];
    customFields: Record<string, unknown>;
    units: { unit: string; factor: number; barcode: string | null }[];
    suppliers: { currentAccountId: string; supplierCode: string | null; isDefault: boolean }[];
    images: { fileId: string }[];
};

export type UpdateVariantInput = {
    code: string;
    barcode: string | null;
    salePriceOverride: Money4 | null;
    purchasePriceOverride: Money4 | null;
};

export type CustomFieldType = "text" | "number" | "date" | "select" | "bool";

export type CustomFieldDto = {
    id: string;
    key: string;
    label: string;
    type: CustomFieldType;
    options: string[];
    required: boolean;
    appliesToProduct: boolean;
    appliesToService: boolean;
    sort: number;
};

export type CustomFieldInput = {
    key?: string;
    type?: CustomFieldType;
    label: string;
    options: string[];
    required: boolean;
    appliesToProduct: boolean;
    appliesToService: boolean;
    sort: number;
};

export type CatalogSettings = {
    codeTemplate: string;
    defaultVatRate: number;
    units: string[];
    barcodePrefix: string;
    nextBarcodeSequence: number;
    nextCodeSequence: number;
};

/** Ürün formunun seçenekleri (katalog.read yeterli): birimler, varsayılan KDV ve özel alan tanımları. */
export type CatalogOptions = { units: string[]; defaultVatRate: number; customFields: CustomFieldDto[] };

export const VAT_RATES = [0, 1, 10, 20];

export type ImportRowResult = {
    rowNumber: number;
    /** "create" | "update" | "error" */
    action: string;
    code: string | null;
    name: string | null;
    errors: string[];
};

export type ImportPreview = {
    batchId: string;
    total: number;
    creates: number;
    updates: number;
    errors: number;
    rows: ImportRowResult[];
};

export type ImportCommitResult = { created: number; updated: number };

export type CommitOutcome = { kind: "done"; result: ImportCommitResult } | { kind: "conflict"; preview: ImportPreview };

export type ExportQuery = { kind?: ProductKind; includeInactive?: boolean };

const base = "/api/erp/catalog";

function queryString(params: Record<string, string | number | boolean | undefined>): string {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
        if (value !== undefined) {
            search.set(key, String(value));
        }
    }
    const text = search.toString();
    return text ? `?${text}` : "";
}

const get = <T>(path: string) => apiFetch<T>(`${base}${path}`);
const send = <T = void>(method: string, path: string, body?: unknown) => apiFetch<T>(`${base}${path}`, { method, body });

/** Ağaçtaki kategorileri sırayla (derinlik bilgisiyle) düz listeye çevirir. */
export function flattenCategories(nodes: CategoryNode[], depth = 0): { node: CategoryNode; depth: number }[] {
    return nodes.flatMap((node) => [{ node, depth }, ...flattenCategories(node.children, depth + 1)]);
}

/** Dosya yanıtını indirir (apiFetch JSON beklediği için ayrı). Hata gövdesi ApiError olur. */
async function download(path: string, fileName: string): Promise<void> {
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
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

export const catalog = {
    options: () => get<CatalogOptions>("/options"),
    categories: () => get<CategoryNode[]>("/categories"),
    createCategory: (name: string, parentId: string | null) => send<CategoryNode>("POST", "/categories", { name, parentId }),
    updateCategory: (id: string, body: { name: string; parentId: string | null; sort: number }) =>
        send<CategoryNode>("PUT", `/categories/${id}`, body),
    deleteCategory: (id: string) => send("DELETE", `/categories/${id}`),

    brands: () => get<BrandDto[]>("/brands"),
    createBrand: (name: string) => send<BrandDto>("POST", "/brands", { name }),
    updateBrand: (id: string, name: string) => send<BrandDto>("PUT", `/brands/${id}`, { name }),
    deleteBrand: (id: string) => send("DELETE", `/brands/${id}`),

    listProducts: (q: ListProductsQuery = {}) =>
        get<PagedResult<ProductListItem>>(
            `/products${queryString({
                search: q.search,
                kind: q.kind,
                categoryId: q.categoryId,
                brandId: q.brandId,
                tag: q.tag,
                includeInactive: q.includeInactive,
                page: q.page,
                pageSize: q.pageSize,
            })}`,
        ),
    nextProductCode: () => get<{ code: string }>("/products/next-code"),
    getProduct: (id: string) => get<ProductDto>(`/products/${id}`),
    createProduct: (body: ProductInput) => send<ProductDto>("POST", "/products", body),
    updateProduct: (id: string, body: ProductInput) => send<ProductDto>("PUT", `/products/${id}`, body),
    setProductActive: (id: string, active: boolean) => send("POST", `/products/${id}/${active ? "activate" : "deactivate"}`),
    deleteProduct: (id: string) => send("DELETE", `/products/${id}`),
    generateBarcode: (id: string) => send<{ barcode: string }>("POST", `/products/${id}/barcode/generate`),
    setAttributes: (id: string, attributes: { name: string; values: string[] }[]) =>
        send<ProductDto>("PUT", `/products/${id}/attributes`, { attributes }),
    updateVariant: (id: string, variantId: string, body: UpdateVariantInput) =>
        send<ProductDto>("PUT", `/products/${id}/variants/${variantId}`, body),
    setVariantActive: (id: string, variantId: string, active: boolean) =>
        send("POST", `/products/${id}/variants/${variantId}/${active ? "activate" : "deactivate"}`),

    customFields: () => get<CustomFieldDto[]>("/custom-fields"),
    createCustomField: (body: CustomFieldInput) => send<CustomFieldDto>("POST", "/custom-fields", body),
    updateCustomField: (id: string, body: CustomFieldInput) => send<CustomFieldDto>("PUT", `/custom-fields/${id}`, body),
    deleteCustomField: (id: string) => send("DELETE", `/custom-fields/${id}`),

    importTemplate: () => download(`${base}/products/import-template.xlsx`, "urun-sablonu.xlsx"),
    previewImport: (file: File, autoCode: boolean) => {
        const form = new FormData();
        form.append("file", file);
        form.append("autoCode", String(autoCode));
        return apiFetch<ImportPreview>(`${base}/products/import/preview`, { method: "POST", body: form });
    },
    /** 409 import_conflict hata olarak değil, taze önizlemeyle birlikte sonuç olarak döner; diğer hatalar fırlatılır. */
    commitImport: async (batchId: string): Promise<CommitOutcome> => {
        try {
            return { kind: "done", result: await apiFetch<ImportCommitResult>(`${base}/products/import/${batchId}/commit`, { method: "POST" }) };
        } catch (e) {
            const preview = e instanceof ApiError && e.code === "import_conflict" ? e.problem?.preview : undefined;
            if (preview) {
                return { kind: "conflict", preview: preview as ImportPreview };
            }
            throw e;
        }
    },
    exportProducts: (q: ExportQuery = {}) =>
        download(`${base}/products/export.xlsx${queryString({ kind: q.kind, includeInactive: q.includeInactive })}`, "urunler.xlsx"),
};
