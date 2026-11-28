import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PackScreen from "./PackScreen";
import { shipmentDto, shipmentLine } from "./shippingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/sevkiyat/paketleme/sv-1"));

function setup() {
    const puts: { parcels: { weightKg: number; widthCm: number; contents: { shipmentLineId: string; qty: number }[] }[] }[] = [];
    const shipment = shipmentDto({ status: "picking", lines: [shipmentLine({ quantity: 4 })] });
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["sevkiyat.read", "sevkiyat.pack", "sevkiyat.ship"] }))),
        http.get("*/api/erp/sales/shipments/sv-1", () => HttpResponse.json(shipment)),
        http.get("*/api/erp/settings/sevkiyat", () => HttpResponse.json({ version: 1, value: { carriers: [] } })),
        http.put("*/api/erp/sales/shipments/sv-1/parcels", async ({ request }) => {
            puts.push((await request.json()) as (typeof puts)[number]);
            return HttpResponse.json({ ...shipment, status: "packed" });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <PackScreen shipmentId="sv-1" />
        </SessionProvider>,
    );
    return { ...view, puts };
}

describe("PackScreen", () => {
    it("PackScreen_DesiLive", async () => {
        const { user } = setup();

        const width = await screen.findByRole("textbox", { name: "Koli 1 en (cm)" });
        await user.type(width, "10");
        await user.type(screen.getByRole("textbox", { name: "Koli 1 boy (cm)" }), "10");
        await user.type(screen.getByRole("textbox", { name: "Koli 1 yükseklik (cm)" }), "10");
        // 10x10x10 / 3000 = 0,33 -> 0,4 (yukarı)
        expect(screen.getByTestId("parcel-1-desi")).toHaveTextContent("0,4 desi");

        await user.clear(width);
        await user.type(width, "30");
        // 30x10x10 / 3000 = 1,0
        expect(screen.getByTestId("parcel-1-desi")).toHaveTextContent("1,0 desi");
        expect(screen.getByTestId("total-desi")).toHaveTextContent("1,0");
    });

    it("PackScreen_AddLineToParcelAndSave", async () => {
        const { user, puts } = setup();

        await user.type(await screen.findByRole("textbox", { name: "Koli 1 ağırlık (kg)" }), "2,5");
        await user.type(screen.getByRole("textbox", { name: "Koli 1 en (cm)" }), "30");
        await user.type(screen.getByRole("textbox", { name: "Koli 1 boy (cm)" }), "20");
        await user.type(screen.getByRole("textbox", { name: "Koli 1 yükseklik (cm)" }), "10");
        // Klavye alternatifi: kalemi seç, koliye ekle.
        await user.click(screen.getByRole("button", { name: "Yün halı kalemini seç" }));
        await user.click(screen.getByRole("button", { name: "Koliye ekle (Koli 1)" }));
        await user.click(screen.getByRole("button", { name: "Paketlemeyi kaydet" }));

        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].parcels).toEqual([{ weightKg: 2.5, widthCm: 30, lengthCm: 20, heightCm: 10, contents: [{ shipmentLineId: "sl-1", qty: 4 }] }]);
    });

    it("PackScreen_UnassignedLinesBlockSave", async () => {
        const { user, puts } = setup();

        await user.type(await screen.findByRole("textbox", { name: "Koli 1 ağırlık (kg)" }), "1");
        await user.click(screen.getByRole("button", { name: "Paketlemeyi kaydet" }));

        expect(await screen.findByRole("alert")).toHaveTextContent("koliye yerleştirilmemiş");
        expect(puts).toHaveLength(0);
    });
});
