import type { MatchRow, PoDto, PoLineDto } from "@/lib/api/purchasing";

export function poLine(overrides: Partial<PoLineDto> = {}): PoLineDto {
    return {
        id: "pol-1",
        sort: 1,
        productId: "prod-1",
        productCode: "U00001",
        productName: "Tişört",
        variantId: null,
        description: "Tişört",
        unit: "adet",
        factor: 1,
        quantity: 10,
        receivedQuantity: 4,
        closedQuantity: 0,
        openQuantity: 6,
        width: null,
        length: null,
        unitPrice: 20,
        discountPercent: null,
        vatRate: 20,
        expectedDate: "2026-03-20",
        salesOrderLineId: null,
        net: 200,
        vat: 40,
        total: 240,
        lastPurchase: { unitPriceTl: 12.5, date: "2026-03-10" },
        ...overrides,
    };
}

export function poDto(overrides: Partial<PoDto> = {}): PoDto {
    return {
        id: "po-1",
        number: "SA2026000001",
        status: "draft",
        supplierId: "sup-1",
        supplierName: "Akın Tekstil",
        branchId: "b-1",
        date: "2026-03-01",
        expectedDate: "2026-03-20",
        currency: "TRY",
        rate: 1,
        notes: null,
        cancelReason: null,
        closeReason: null,
        gross: 200,
        discount: 0,
        net: 200,
        vat: 40,
        total: 240,
        version: 1,
        lines: [poLine()],
        matchRows: [],
        receipts: [],
        invoices: [],
        ...overrides,
    };
}

export function matchRow(overrides: Partial<MatchRow> = {}): MatchRow {
    return { poLineId: "pol-1", ordered: 10, received: 10, invoiced: 10, poPrice: 20, receiptPrice: 20, invoicePrice: 20, qtyMismatch: false, priceVariance: false, ...overrides };
}
