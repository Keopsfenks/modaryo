import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { accountDto, addressDto } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ShipmentForm from "./ShipmentForm";
import { orderDto, orderLine } from "./salesFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

type Posted = { orderId: string; deliveryAddressId: string | null; lines: { orderLineId: string; quantity: number }[] };

function setup() {
    const posted: Posted[] = [];
    server.use(
        http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([])),
        http.get("*/api/erp/current-accounts/acc-1", () =>
            HttpResponse.json(accountDto({ addresses: [addressDto({ id: "addr-ship", title: "Depo", isShipping: true, isDefaultShipping: true })] })),
        ),
        http.get("*/api/erp/catalog/products/*", () => HttpResponse.json({}, { status: 404 })),
        http.post("*/api/erp/sales/shipments", async ({ request }) => {
            posted.push((await request.json()) as Posted);
            return HttpResponse.json({ id: "sv-1", number: "SV2026000001", lines: [] }, { status: 201 });
        }),
    );
    const order = orderDto({
        status: "approved",
        lines: [
            orderLine({ id: "ol-1", productName: "Yün halı", quantity: 3.68, openQuantity: 3.68 }),
            orderLine({ id: "ol-2", productId: "prod-2", productCode: "KIL01", productName: "Kilim", quantity: 2, shippedQuantity: 2, openQuantity: 0, width: null, length: null }),
            orderLine({ id: "ol-3", productId: "prod-3", productCode: "PAS01", productName: "Paspas", quantity: 10, shippedQuantity: 4, openQuantity: 6, width: null, length: null }),
        ],
    });
    const onDone = vi.fn();
    const view = renderWithTheme(<ShipmentForm order={order} onClose={() => undefined} onDone={onDone} />);
    return { ...view, posted, onDone };
}

describe("ShipmentForm", () => {
    it("ShipmentForm_PrefillsOpen", async () => {
        const { user, posted, onDone } = setup();

        // Açık miktarlar önceden dolar; açığı kalmayan satır listelenmez.
        expect(screen.getByRole("textbox", { name: "Yün halı sevk miktarı" })).toHaveValue("3,68");
        expect(screen.getByRole("textbox", { name: "Paspas sevk miktarı" })).toHaveValue("6");
        expect(screen.queryByRole("textbox", { name: "Kilim sevk miktarı" })).not.toBeInTheDocument();
        // Varsayılan teslimat adresi seçili gelir.
        await waitFor(() => expect(screen.getByRole("combobox", { name: "Teslimat adresi" })).toHaveValue("addr-ship"));

        const paspas = screen.getByRole("textbox", { name: "Paspas sevk miktarı" });
        await user.clear(paspas);
        await user.type(paspas, "2");
        await user.click(screen.getByRole("button", { name: "Sevk et" }));

        await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
        expect(posted).toHaveLength(1);
        expect(posted[0].deliveryAddressId).toBe("addr-ship");
        expect(posted[0].lines).toEqual([
            { orderLineId: "ol-1", quantity: 3.68 },
            { orderLineId: "ol-3", quantity: 2 },
        ]);
    });

    it("ShipmentForm_ExceedsOpenBlocked", async () => {
        const { user, posted, onDone } = setup();

        const paspas = screen.getByRole("textbox", { name: "Paspas sevk miktarı" });
        await user.clear(paspas);
        await user.type(paspas, "7");
        await user.click(screen.getByRole("button", { name: "Sevk et" }));

        expect(await screen.findByText(/Sevk miktarı siparişin açık miktarını aşıyor/)).toBeInTheDocument();
        expect(posted).toHaveLength(0);
        expect(onDone).not.toHaveBeenCalled();
    });
});
