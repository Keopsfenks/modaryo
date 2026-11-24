import { apiFetch } from "./client";
import type { Currency, PagedResult } from "./erp";
import type { Money4 } from "./catalog";

// ERP servisi (services/erp) fiyatlandırma uç noktalarının tipli sarmalayıcıları (/api/erp/pricing/*).
// Enum'lar backend'de camelCase string, tarihler "YYYY-MM-DD", saatler "HH:mm:ss" olarak gider.

export type PriceListKind = "sale" | "purchase";
export type PriceTier = { minQty: number; price: number };
export type PriceSource = "customerPrice" | "groupList" | "defaultList" | "supplierList" | "cardDefault" | "none";
export type RoundingRule = "none" | "cent" | "fiveCents" | "tenCents" | "fiftyCents" | "one" | "nineEnding";
export type CampaignKind = "buyXPayY" | "percentOff";
export type CampaignDay = "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday" | "sunday";

export type PriceListDto = {
    id: string;
    name: string;
    kind: PriceListKind;
    currency: Currency;
    vatIncluded: boolean;
    validFrom: string | null;
    validTo: string | null;
    isDefault: boolean;
    supplierId: string | null;
    baseListId: string | null;
    adjustmentPercent: number | null;
    isActive: boolean;
    version: number;
    itemCount: number;
};

export type PriceListInput = {
    name: string;
    kind: PriceListKind;
    currency: Currency;
    vatIncluded: boolean;
    validFrom: string | null;
    validTo: string | null;
    supplierId: string | null;
    baseListId: string | null;
    adjustmentPercent: number | null;
    isActive: boolean;
};

export type PriceItemDto = {
    id: string | null;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    variantCode: string | null;
    unit: string;
    tiers: PriceTier[];
    version: number;
    /** Türetilmiş listede açık satırı olmayan kalem: kademeler taban listeden hesaplanmıştır. */
    derived: boolean;
};

export type PriceRowInput = {
    productId: string;
    variantId: string | null;
    unit: string;
    tiers: PriceTier[];
    version?: number;
};

export type CustomerGroupDto = { id: string; name: string; salePriceListId: string | null; customerCount: number };

export type CustomerPriceDto = {
    id: string;
    currentAccountId: string;
    productId: string;
    productCode: string;
    productName: string;
    variantId: string | null;
    variantCode: string | null;
    unit: string;
    currency: Currency;
    vatIncluded: boolean;
    tiers: PriceTier[];
    validFrom: string | null;
    validTo: string | null;
};

export type CustomerPriceInput = {
    productId: string;
    variantId: string | null;
    unit: string;
    currency: Currency;
    vatIncluded: boolean;
    tiers: PriceTier[];
    validFrom: string | null;
    validTo: string | null;
};

export type PriceHistoryScope = "list" | "customer" | "card";

export type PriceHistoryDto = {
    id: string;
    at: string;
    actorLabel: string;
    scope: PriceHistoryScope;
    scopeId: string;
    productId: string;
    productCode: string | null;
    variantId: string | null;
    unit: string;
    /** Liste ve müşteri kayıtlarında kademe dizisi, kart kayıtlarında Money4; yoksa null. */
    before: PriceTier[] | Money4 | null;
    after: PriceTier[] | Money4 | null;
    kind: PriceListKind;
};

export type PriceHistoryQuery = { productId?: string; variantId?: string; scope?: PriceHistoryScope; page?: number; pageSize?: number };

export type PriceQuote = {
    unitPrice: number;
    currency: Currency;
    vatIncluded: boolean;
    source: PriceSource;
    sourceId: string | null;
    tierMinQty: number;
};

export type QuoteQuery = {
    kind?: PriceListKind;
    currentAccountId?: string;
    productId: string;
    variantId?: string;
    unit: string;
    quantity: number;
    date?: string;
};

export type CampaignDto = {
    id: string;
    name: string;
    kind: CampaignKind;
    buyQty: number | null;
    payQty: number | null;
    percent: number | null;
    validFrom: string | null;
    validTo: string | null;
    days: CampaignDay[] | null;
    fromTime: string | null;
    toTime: string | null;
    productIds: string[];
    variantIds: string[];
    categoryIds: string[];
    isActive: boolean;
};

export type CampaignInput = Omit<CampaignDto, "id">;

export type PricingSettings = {
    defaultSaleListId: string | null;
    defaultPurchaseListId: string | null;
    showVatIncluded: boolean;
    rounding: RoundingRule;
};

const base = "/api/erp/pricing";

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

export const pricing = {
    lists: (q: { kind?: PriceListKind; includeInactive?: boolean } = {}) =>
        get<PriceListDto[]>(`/lists${queryString({ kind: q.kind, includeInactive: q.includeInactive })}`),
    getList: (id: string) => get<PriceListDto>(`/lists/${id}`),
    createList: (body: PriceListInput) => send<PriceListDto>("POST", "/lists", body),
    updateList: (id: string, body: PriceListInput & { version?: number }) => send<PriceListDto>("PUT", `/lists/${id}`, body),
    deleteList: (id: string) => send("DELETE", `/lists/${id}`),

    listItems: (id: string, q: { search?: string; page?: number; pageSize?: number } = {}) =>
        get<PagedResult<PriceItemDto>>(`/lists/${id}/items${queryString({ search: q.search, page: q.page, pageSize: q.pageSize })}`),
    upsertItems: (id: string, rows: PriceRowInput[]) => send<{ rows: PriceItemDto[] }>("PUT", `/lists/${id}/items`, { rows }),
    deleteItem: (id: string, itemId: string) => send("DELETE", `/lists/${id}/items/${itemId}`),
    bulkAdjust: (id: string, body: { percent: number; categoryId: string | null; brandId: string | null }) =>
        send<{ updated: number }>("POST", `/lists/${id}/bulk-adjust`, body),

    customerGroups: () => get<CustomerGroupDto[]>("/customer-groups"),
    createGroup: (body: { name: string; salePriceListId: string | null }) => send<CustomerGroupDto>("POST", "/customer-groups", body),
    updateGroup: (id: string, body: { name: string; salePriceListId: string | null }) =>
        send<CustomerGroupDto>("PUT", `/customer-groups/${id}`, body),
    deleteGroup: (id: string) => send("DELETE", `/customer-groups/${id}`),

    customerPrices: (accountId: string) => get<CustomerPriceDto[]>(`/customers/${accountId}/prices`),
    createCustomerPrice: (accountId: string, body: CustomerPriceInput) =>
        send<CustomerPriceDto>("POST", `/customers/${accountId}/prices`, body),
    updateCustomerPrice: (accountId: string, id: string, body: CustomerPriceInput) =>
        send<CustomerPriceDto>("PUT", `/customers/${accountId}/prices/${id}`, body),
    deleteCustomerPrice: (accountId: string, id: string) => send("DELETE", `/customers/${accountId}/prices/${id}`),

    history: (q: PriceHistoryQuery = {}) =>
        get<PagedResult<PriceHistoryDto>>(
            `/history${queryString({ productId: q.productId, variantId: q.variantId, scope: q.scope, page: q.page, pageSize: q.pageSize })}`,
        ),
    quote: (q: QuoteQuery) =>
        get<PriceQuote>(
            `/quote${queryString({
                kind: q.kind,
                currentAccountId: q.currentAccountId,
                productId: q.productId,
                variantId: q.variantId,
                unit: q.unit,
                quantity: q.quantity,
                date: q.date,
            })}`,
        ),

    campaigns: (includeInactive = false) => get<CampaignDto[]>(`/campaigns${queryString({ includeInactive: includeInactive || undefined })}`),
    createCampaign: (body: CampaignInput) => send<CampaignDto>("POST", "/campaigns", body),
    updateCampaign: (id: string, body: CampaignInput) => send<CampaignDto>("PUT", `/campaigns/${id}`, body),
    deleteCampaign: (id: string) => send("DELETE", `/campaigns/${id}`),
};

export const SOURCE_LABELS: Record<PriceSource, string> = {
    customerPrice: "Müşteriye özel fiyat",
    groupList: "Müşteri grubu listesi",
    defaultList: "Varsayılan liste",
    supplierList: "Tedarikçi listesi",
    cardDefault: "Ürün kartı fiyatı",
    none: "Fiyat yok",
};
