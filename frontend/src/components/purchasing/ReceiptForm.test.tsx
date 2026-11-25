import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { poDto, poLine } from "./purchasingFixtures";
import ReceiptForm, { matchScanned } from "./ReceiptForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const product = {
    id: "prod-1",
    code: "U00001",
    name: "Tişört",
    baseUnit: "adet",
    units: [],
    tracksSerialLot: false,
    trackingMode: null,
};

const item = { productId: "prod-1", variantId: null, code: "U00001", name: "Tişört", baseUnit: "adet", units: [{ unit: "adet", factor: 1, barcode: "8690001" }], tracksSerialLot: false, vatRate: 20 };

function setup(po = poDto({ status: "approved" })) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read", "satinalma.receive"] }))),
        http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
        http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([{ id: "w-1", branchId: "b-1", code: "D1", name: "Ana depo", kind: "normal", isDefault: true, isActive: true, inUse: false }])),
        http.get("*/api/erp/purchasing/orders/po-1", () => HttpResponse.json(po)),
        http.get("*/api/erp/catalog/products/prod-1", () => HttpResponse.json(product)),
        http.get("*/api/erp/catalog/items", () => HttpResponse.json([item])),
    );
    return renderWithTheme(
        <SessionProvider>
            <ReceiptForm poId="po-1" />
        </SessionProvider>,
    );
}

beforeEach(() => resetNavigation("/satin-alma/mal-kabul/yeni"));

describe("ReceiptForm", () => {
    it("ReceiptForm_PrefillsRemaining", async () => {
        setup();

        expect(await screen.findByLabelText("U00001 miktar")).toHaveValue("6");
    });

    it("ReceiptForm_BarcodeIncrements", async () => {
        const { user } = setup();
        await screen.findByLabelText("U00001 miktar");

        await user.type(screen.getByLabelText("Barkod"), "8690001{Enter}");

        await waitFor(() => expect(screen.getByLabelText("U00001 miktar")).toHaveValue("7"));
        expect(screen.getByText(/Açık miktarı aşıyor/)).toBeInTheDocument();
    });

    it("ReceiptForm_OverDeliveryError", async () => {
        let body: { purchaseOrderId?: string; lines?: { poLineId: string; quantity: number }[] } = {};
        server.use(
            http.post("*/api/erp/purchasing/receipts", async ({ request }) => {
                body = (await request.json()) as typeof body;
                return HttpResponse.json(problem(409, "Gelen miktar aşıyor.", "over_delivery"), { status: 409 });
            }),
        );
        const { user } = setup();
        const quantity = await screen.findByLabelText("U00001 miktar");
        await user.clear(quantity);
        await user.type(quantity, "9");

        await user.click(screen.getByRole("button", { name: "Mal kabulü kaydet" }));

        expect(await screen.findByText("Gelen miktar sipariş miktarını ve izin verilen toleransı aşıyor.")).toBeInTheDocument();
        expect(body.purchaseOrderId).toBe("po-1");
        expect(body.lines?.[0]).toMatchObject({ poLineId: "pol-1", quantity: 9 });
    });

    it("ReceiptForm_ToleranceWarningShownAfterSave", async () => {
        server.use(
            http.post("*/api/erp/purchasing/receipts", () =>
                HttpResponse.json({ id: "r-1", number: "MK2026000001", warnings: ["over_delivery_within_tolerance"], lines: [] }, { status: 201 }),
            ),
        );
        const { user } = setup(poDto({ status: "approved", lines: [poLine()] }));
        await screen.findByLabelText("U00001 miktar");

        await user.click(screen.getByRole("button", { name: "Mal kabulü kaydet" }));

        expect(await screen.findByText(/tolerans dahilinde kabul edildi/)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Siparişe dön" })).toHaveAttribute("href", "/satin-alma/siparisler/po-1");
    });
});

describe("matchScanned", () => {
    it("matchScanned_PrefersExactBarcode", () => {
        const other = { ...item, productId: "p2", code: "U2", units: [{ unit: "adet", factor: 1, barcode: "111" }] };
        expect(matchScanned([other, item] as never, "8690001")?.productId).toBe("prod-1");
        expect(matchScanned([other, item] as never, "zzz")).toBeNull();
    });
});
