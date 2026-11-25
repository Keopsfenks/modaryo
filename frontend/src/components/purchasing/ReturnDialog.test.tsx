import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReceiptDto, ReceiptLineDto } from "@/lib/api/purchasing";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ReturnDialog from "./ReturnDialog";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const line = (overrides: Partial<ReceiptLineDto>): ReceiptLineDto => ({
    id: "rl-1",
    sort: 1,
    poLineId: null,
    productId: "p-1",
    productCode: "U1",
    productName: "Süt",
    variantId: null,
    unit: "koli",
    quantity: 2,
    baseQuantity: 24,
    unitPrice: 10,
    unitCostTl: 10,
    warehouseId: "w-1",
    quarantine: "none",
    targetWarehouseId: null,
    ...overrides,
});

const receipt = (lines: ReceiptLineDto[]): ReceiptDto => ({
    id: "r-1",
    number: "MK2026000001",
    status: "posted",
    purchaseOrderId: null,
    purchaseOrderNumber: null,
    supplierId: "s-1",
    supplierName: "Akın Tekstil",
    branchId: "b-1",
    warehouseId: "w-1",
    date: "2026-03-05",
    currency: "TRY",
    rate: 1,
    deliveryNoteNo: null,
    notes: null,
    cancelReason: null,
    total: 240,
    invoiced: false,
    warnings: [],
    lines,
});

type Body = { lines: { receiptLineId: string; quantity: number; lots?: { lotNo: string; quantity: number; lotId?: string | null }[]; serials?: string[] }[] };

function setup(lines: ReceiptLineDto[], tracked: Record<string, "lot" | "serial" | null>) {
    let sent: Body | null = null;
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read", "satinalma.manage"] }))),
        http.get("*/api/erp/catalog/products/:id", ({ params }) => {
            const mode = tracked[params.id as string] ?? null;
            return HttpResponse.json({ id: params.id, tracksSerialLot: mode !== null, trackingMode: mode });
        }),
        http.get("*/api/erp/inventory/tracking/lots/suggest", () => HttpResponse.json([{ lotId: "lot-1", lotNo: "L1", expiry: "2026-12-01", quantity: 24 }])),
        http.get("*/api/erp/inventory/tracking/serials", () => HttpResponse.json([{ id: "se-1", serial: "S1", warehouseId: "w-1", inStock: true }])),
        http.post("*/api/erp/purchasing/returns", async ({ request }) => {
            sent = (await request.json()) as Body;
            return HttpResponse.json({ returnId: "ret-1", receiptId: "r-1", invoiceId: null, invoiceNumber: null }, { status: 201 });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <ReturnDialog receipt={receipt(lines)} onClose={() => undefined} onDone={() => undefined} />
        </SessionProvider>,
    );
    return { ...view, sent: () => sent };
}

beforeEach(() => resetNavigation("/satin-alma/mal-kabul"));

describe("ReturnDialog", () => {
    it("Return_TrackedLine_RequiresLotAndSendsBaseQuantities", async () => {
        const { user, sent } = setup([line({})], { "p-1": "lot" });

        await user.type(await screen.findByRole("textbox", { name: "U1 iade miktarı" }), "2");
        await user.type(screen.getByRole("textbox", { name: "Gerekçe" }), "Bozuk ürün");
        await user.click(screen.getByRole("button", { name: "İade et" }));
        expect(await screen.findByText(/U1: lot bilgisini girin/i)).toBeInTheDocument();
        expect(sent()).toBeNull();

        await user.click(screen.getByRole("button", { name: "Lot (0)" }));
        await screen.findByDisplayValue("L1");
        await user.click(screen.getByRole("button", { name: "Tamam" }));
        expect(screen.getByRole("button", { name: "Lot (1)" })).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "İade et" }));

        await waitFor(() => expect(sent()).not.toBeNull());
        // Lot miktarı iade satırının biriminde (2 koli) seçilir; sunucuya temel birimde (24 adet) gider.
        expect(sent()!.lines).toEqual([{ receiptLineId: "rl-1", quantity: 2, lots: [{ lotNo: "L1", expiry: "2026-12-01", quantity: 24, lotId: "lot-1" }] }]);
    });

    it("Return_SerialLine_SendsSelectedSerials", async () => {
        const { user, sent } = setup([line({ quantity: 1, baseQuantity: 1, unit: "adet" })], { "p-1": "serial" });

        await user.type(await screen.findByRole("textbox", { name: "U1 iade miktarı" }), "1");
        await user.type(screen.getByRole("textbox", { name: "Gerekçe" }), "Arızalı");
        await user.click(screen.getByRole("button", { name: "Seri (0)" }));
        await user.click(await screen.findByRole("combobox", { name: "Seri numaraları" }));
        await user.click(await screen.findByRole("option", { name: "S1" }));
        await user.click(screen.getByRole("button", { name: "Tamam" }));
        await user.click(screen.getByRole("button", { name: "İade et" }));

        await waitFor(() => expect(sent()).not.toBeNull());
        expect(sent()!.lines).toEqual([{ receiptLineId: "rl-1", quantity: 1, serials: ["S1"] }]);
    });

    it("Return_UntrackedLine_HasNoLotButton", async () => {
        const { user, sent } = setup([line({ unit: "adet", quantity: 3, baseQuantity: 3 })], { "p-1": null });

        await user.type(await screen.findByRole("textbox", { name: "U1 iade miktarı" }), "1");
        await user.type(screen.getByRole("textbox", { name: "Gerekçe" }), "Fazla");
        expect(screen.queryByRole("button", { name: /^(Lot|Seri)/ })).not.toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "İade et" }));

        await waitFor(() => expect(sent()).not.toBeNull());
        expect(sent()!.lines).toEqual([{ receiptLineId: "rl-1", quantity: 1 }]);
    });

    it("Return_ReleasedLine_PicksFromTargetWarehouse", async () => {
        let warehouse = "";
        const { user } = setup([line({ quarantine: "released", warehouseId: "w-q", targetWarehouseId: "w-1" })], { "p-1": "lot" });
        server.use(
            http.get("*/api/erp/inventory/tracking/lots/suggest", ({ request }) => {
                warehouse = new URL(request.url).searchParams.get("warehouseId") ?? "";
                return HttpResponse.json([]);
            }),
        );

        await user.type(await screen.findByRole("textbox", { name: "U1 iade miktarı" }), "2");
        await user.click(screen.getByRole("button", { name: "Lot (0)" }));

        await waitFor(() => expect(warehouse).toBe("w-1"));
    });
});
