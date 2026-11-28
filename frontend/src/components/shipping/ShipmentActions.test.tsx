import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ShipmentActions from "./ShipmentActions";
import { shipmentDto, shipmentLine } from "./shippingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/sevkiyat"));

function setup(shipment: Parameters<typeof ShipmentActions>[0]["shipment"], permissions: string[]) {
    const calls: string[] = [];
    const changed: unknown[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.post("*/api/erp/sales/shipments/sv-1/unpick", () => {
            calls.push("unpick");
            return HttpResponse.json(shipmentDto({ status: "draft" }));
        }),
        http.post("*/api/erp/sales/shipments/sv-1/cancel", async ({ request }) => {
            calls.push(`cancel:${((await request.json()) as { reason: string }).reason}`);
            return HttpResponse.json(shipmentDto({ status: "cancelled" }));
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <ShipmentActions shipment={shipment} onChange={(s) => changed.push(s)} />
        </SessionProvider>,
    );
    return { ...view, calls, changed };
}

describe("ShipmentActions", () => {
    it("Actions_PackedShipment_CanBeUnpickedAndCancelled", async () => {
        const { user, calls, changed } = setup(shipmentDto({ status: "packed" }), ["sevkiyat.read", "sevkiyat.pack", "sevkiyat.ship"]);

        await user.click(await screen.findByRole("button", { name: "Toplamayı geri al" }));
        await waitFor(() => expect(calls).toEqual(["unpick"]));
        expect(changed).toHaveLength(1);

        await user.click(screen.getByRole("button", { name: "Sevki iptal et" }));
        await user.type(await screen.findByRole("textbox"), "Müşteri vazgeçti");
        await user.click(screen.getByRole("button", { name: "İptal et" }));
        await waitFor(() => expect(calls).toEqual(["unpick", "cancel:Müşteri vazgeçti"]));
    });

    it("Actions_PickingShipment_ShowsUnpickForPickRole", async () => {
        setup(shipmentDto({ status: "picking", pickListId: "pk-1" }), ["sevkiyat.read", "sevkiyat.pick"]);

        expect(await screen.findByRole("button", { name: "Toplamayı geri al" })).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Sevki iptal et" })).not.toBeInTheDocument();
    });

    it("Actions_ServiceOnlyDraft_OffersDirectShipNotPicking", async () => {
        setup(shipmentDto({ status: "draft", lines: [shipmentLine({ isService: true })] }), ["sevkiyat.read", "sevkiyat.pick", "sevkiyat.ship"]);

        expect(await screen.findByRole("button", { name: "Doğrudan sevk et" })).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Toplamaya başla" })).not.toBeInTheDocument();
    });

    it("Actions_InvoicedReturn_HiddenWithoutSalesReturn", async () => {
        const failed = shipmentDto({ status: "deliveryFailed", invoiceId: "inv-1" });
        const { unmount } = setup(failed, ["sevkiyat.ship"]);
        expect(await screen.findByRole("button", { name: "Yeniden gönder" })).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "İade al" })).not.toBeInTheDocument();
        unmount();

        setup(failed, ["sevkiyat.ship", "satis.return"]);
        expect(await screen.findByRole("button", { name: "İade al" })).toBeInTheDocument();
    });
});
