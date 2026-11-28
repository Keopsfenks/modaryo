import type { ShipmentDto, ShipmentLineDto } from "@/lib/api/sales";
import type { CarrierInfo, PickLineDto, PickListDto } from "@/lib/api/shipping";

export const CARRIERS: CarrierInfo[] = [
    { code: "yurtici", name: "Yurtiçi", trackingUrlTemplate: "https://kargo.example/yurtici?code={no}", isActive: true },
    { code: "kendi-arac", name: "Kendi aracımız", trackingUrlTemplate: null, isActive: true },
];

export function shipmentLine(overrides: Partial<ShipmentLineDto> = {}): ShipmentLineDto {
    return {
        id: "sl-1",
        sort: 1,
        salesOrderLineId: "ol-1",
        productId: "prod-1",
        variantId: null,
        description: "Yün halı",
        unit: "adet",
        factor: 1,
        quantity: 4,
        width: null,
        length: null,
        invoicedQuantity: 0,
        isService: false,
        ...overrides,
    };
}

export function shipmentDto(overrides: Partial<ShipmentDto> = {}): ShipmentDto {
    return {
        id: "sv-1",
        number: "SV2026000001",
        status: "draft",
        salesOrderId: "so-1",
        orderNumber: "SS2026000001",
        currentAccountId: "acc-1",
        customerName: "Akın Tekstil",
        branchId: "b-1",
        warehouseId: "wh-1",
        date: "2026-11-28",
        shippedAt: null,
        deliveredAt: null,
        receivedBy: null,
        deliveryAddress: null,
        cargoNote: null,
        notes: null,
        cancelReason: null,
        invoiceId: null,
        version: 1,
        lines: [shipmentLine()],
        carrierCode: null,
        trackingNo: null,
        irsaliyeNo: null,
        dispatchAt: null,
        vehicle: null,
        driver: null,
        pickListId: null,
        totalWeightKg: 0,
        totalDesi: 0,
        parcels: [],
        attempts: [],
        carrierName: null,
        trackingUrl: null,
        customerEmailMissing: false,
        returnId: null,
        proof: null,
        ...overrides,
    };
}

export function pickLine(overrides: Partial<PickLineDto> = {}): PickLineDto {
    return {
        id: "pl-1",
        sort: 1,
        warehouseId: "wh-1",
        productId: "prod-1",
        variantId: null,
        code: "HALI01",
        variantCode: null,
        variantLabel: null,
        trackingMode: "none",
        barcodes: [],
        description: "Yün halı",
        unit: "adet",
        factor: 1,
        quantity: 3,
        pickedQuantity: null,
        location: null,
        suggestedLots: null,
        pickedLots: null,
        pickedSerials: null,
        sources: null,
        ...overrides,
    };
}

export function pickList(overrides: Partial<PickListDto> = {}): PickListDto {
    return {
        id: "pk-1",
        number: "TL2026000001",
        status: "open",
        branchId: "b-1",
        warehouseId: "wh-1",
        warehouseName: "Merkez depo",
        shipments: [{ id: "sv-1", number: "SV2026000001", orderNumber: "SS2026000001", customerName: "Akın Tekstil" }],
        version: 1,
        lines: [pickLine(), pickLine({ id: "pl-2", sort: 2, productId: "prod-2", code: "KIL01", description: "Kilim", quantity: 1 })],
        ...overrides,
    };
}
