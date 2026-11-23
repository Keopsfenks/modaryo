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
    exportProducts: (q: ExportQuery = {}) =>
        download(`${base}/products/export.xlsx${queryString({ kind: q.kind, includeInactive: q.includeInactive })}`, "urunler.xlsx"),
};
