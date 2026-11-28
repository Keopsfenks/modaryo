import type { SellableItem } from "@/lib/api/catalog";
import type { PosLookup, RegisterDto, SaleDto, SaleResult, ShiftDto, ShiftReport, SlipDto } from "@/lib/api/pos";

export const REGISTER: RegisterDto = {
    id: "reg-1",
    code: "K1",
    name: "Kasa 1",
    branchId: "br-1",
    warehouseId: "wh-1",
    cashAccountId: "acc-cash",
    cardAccountId: "acc-card",
    isActive: true,
    version: 1,
    pages: [[{ productId: "p1", variantId: null, code: "U00001", name: "Kalem", presetQty: null, color: "#1976d2" }]],
};

export const SHIFT: ShiftDto = {
    id: "sh-1",
    registerId: "reg-1",
    registerCode: "K1",
    cashierUserId: "u1",
    status: "open",
    openedAt: "2026-11-28T08:00:00+03:00",
    openingCash: 500,
    closedAt: null,
    countedCash: null,
    expectedCash: null,
    difference: null,
    closeNote: null,
    version: 1,
};

export function item(overrides: Partial<SellableItem> = {}): SellableItem {
    return {
        productId: "p1",
        variantId: null,
        kind: "product",
        code: "U00001",
        name: "Kalem",
        baseUnit: "adet",
        vatRate: 20,
        soldByMeasure: false,
        measureUnit: null,
        tracksSerialLot: false,
        salePrice: null,
        purchasePrice: null,
        units: [],
        isActive: true,
        ...overrides,
    };
}

export function lookup(overrides: Partial<PosLookup> = {}): PosLookup {
    return { item: item(), unit: "adet", factor: 1, quantity: 1, unitPrice: 25, vatIncluded: true, fixedTotal: null, campaign: null, otherStores: [], ...overrides };
}

export function slip(overrides: Partial<SlipDto> = {}): SlipDto {
    return {
        firm: "Demo Firma",
        branch: "Merkez",
        register: "Kasa 1",
        cashier: "Ali Veli",
        number: "PSK1202611280001",
        at: "2026-11-28T10:15:00+03:00",
        mode: "fiscalReceipt",
        lines: [{ description: "Kalem", quantity: 2, unit: "adet", unitPrice: 25, discount: 0, campaignDiscount: 0, vatRate: 20, total: 50 }],
        vatSummary: [{ rate: 20, base: 41.67, vat: 8.33 }],
        subtotal: 50,
        discount: 0,
        total: 50,
        payments: [{ method: "cash", amount: 100 }],
        change: 50,
        invoiceNumber: null,
        barcode: "PSK1202611280001",
        notes: [],
        ...overrides,
    };
}

export function saleResult(overrides: Partial<SaleResult> = {}): SaleResult {
    return { id: "sale-1", number: "PSK1202611280001", total: 50, change: 0, invoiceNumber: null, replayed: false, slip: slip(), ...overrides };
}

export function saleDto(overrides: Partial<SaleDto> = {}): SaleDto {
    return {
        id: "sale-1",
        number: "PSK1202611280001",
        status: "completed",
        registerId: "reg-1",
        shiftId: "sh-1",
        currentAccountId: "ca-1",
        date: "2026-11-28",
        at: "2026-11-28T10:15:00+03:00",
        mode: "fiscalReceipt",
        subtotal: 50,
        discount: 0,
        vatTotal: 8.33,
        total: 50,
        change: 0,
        invoiceId: null,
        invoiceNumber: null,
        cancelReason: null,
        lines: [
            { id: "line-1", sort: 1, productId: "p1", variantId: null, description: "Kalem", unit: "adet", quantity: 2, unitPrice: 25, vatRate: 20, discount: 0, campaignDiscount: 0, net: 41.67, vat: 8.33, total: 50, returnedQuantity: 0 },
        ],
        payments: [{ method: "cash", amount: 50 }],
        slip: slip(),
        ...overrides,
    };
}

export function shiftReport(overrides: Partial<ShiftReport["totals"]> = {}, shift: Partial<ShiftDto> = {}): ShiftReport {
    return {
        shift: { ...SHIFT, ...shift },
        totals: {
            salesCount: 4,
            salesTotal: 900,
            cashSalesNet: 700,
            cardSales: 200,
            onAccountSales: 0,
            returnCreditSales: 0,
            changeGiven: 20,
            cancelledCount: 0,
            cancelledTotal: 0,
            returnsCount: 0,
            returnsTotal: 0,
            cashRefunds: 0,
            cardRefunds: 0,
            cashIn: 0,
            cashOut: 0,
            campaignDiscount: 0,
            expectedCash: 1250.5,
            ...overrides,
        },
        vatSummary: [{ rate: 20, base: 750, vat: 150 }],
        firstReceipt: "PSK1202611280001",
        lastReceipt: "PSK1202611280004",
        cashMoves: [],
    };
}
