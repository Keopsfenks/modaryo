import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import QuarantineList from "./QuarantineList";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const row = {
    receiptLineId: "rl-1",
    receiptId: "r-1",
    receiptNumber: "MK2026000001",
    date: "2026-03-05",
    supplierName: "Akın Tekstil",
    purchaseOrderId: "po-1",
    productId: "prod-1",
    productCode: "U00001",
    productName: "Tişört",
    variantId: null,
    unit: "adet",
    receivedQuantity: 10,
    openQuantity: 10,
    openBaseQuantity: 10,
    warehouseId: "w-q",
    targetWarehouseId: "w-1",
};

const warehouses = [
    { id: "w-q", branchId: "b-1", code: "KRN", name: "Karantina", kind: "quarantine", isDefault: false, isActive: true, inUse: true },
    { id: "w-1", branchId: "b-1", code: "D1", name: "Ana depo", kind: "normal", isDefault: true, isActive: true, inUse: true },
];

function setup(permissions = ["satinalma.read", "satinalma.quarantine"]) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json(warehouses)),
        http.get("*/api/erp/purchasing/quarantine", () => HttpResponse.json([row])),
    );
    return renderWithTheme(
        <SessionProvider>
            <QuarantineList />
        </SessionProvider>,
    );
}

beforeEach(() => resetNavigation("/satin-alma/karantina"));

describe("QuarantineList", () => {
    it("Quarantine_ReleaseAndReject", async () => {
        const calls: { path: string; body: unknown }[] = [];
        server.use(
            http.post("*/api/erp/purchasing/quarantine/release", async ({ request }) => {
                calls.push({ path: "release", body: await request.json() });
                return HttpResponse.json({ receiptLineId: "rl-1", state: "held", openQuantity: 6, returnId: null, invoiceId: null, invoiceNumber: null });
            }),
            http.post("*/api/erp/purchasing/quarantine/reject", async ({ request }) => {
                calls.push({ path: "reject", body: await request.json() });
                return HttpResponse.json({ receiptLineId: "rl-1", state: "held", openQuantity: 2, returnId: "ret-1", invoiceId: null, invoiceNumber: null });
            }),
        );
        const { user } = setup();

        await user.click(await screen.findByRole("button", { name: "Serbest bırak" }));
        let dialog = await screen.findByRole("dialog");
        const quantity = within(dialog).getByLabelText("Miktar");
        await user.clear(quantity);
        await user.type(quantity, "4");
        await user.click(within(dialog).getByRole("button", { name: "Serbest bırak" }));
        await waitFor(() => expect(calls).toHaveLength(1));
        expect(calls[0]).toEqual({ path: "release", body: { receiptLineId: "rl-1", quantity: 4, targetWarehouseId: "w-1" } });
        expect(await screen.findByText(/4 adet depoya alındı/)).toBeInTheDocument();

        await user.click(await screen.findByRole("button", { name: "Reddet" }));
        dialog = await screen.findByRole("dialog");
        await user.click(within(dialog).getByLabelText("Tedarikçiye iade et"));
        await user.click(within(dialog).getByRole("button", { name: "Reddet" }));
        await waitFor(() => expect(calls).toHaveLength(2));
        expect(calls[1]).toEqual({ path: "reject", body: { receiptLineId: "rl-1", quantity: 10, action: "return" } });
    });

    it("Quarantine_ReadOnlyUser_HasNoActions", async () => {
        setup(["satinalma.read"]);

        expect(await screen.findByText("MK2026000001")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Serbest bırak" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Reddet" })).not.toBeInTheDocument();
    });
});
