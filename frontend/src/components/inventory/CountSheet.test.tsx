import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sellable, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CountSheet from "./CountSheet";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const count = (overrides: Record<string, unknown> = {}) => ({
    id: "c-1",
    number: "SY2026000001",
    name: "Kasım sayımı",
    warehouseId: "w-1",
    status: "open",
    date: "2026-11-24",
    snapshotAt: "2026-11-24T08:00:00Z",
    postedAt: null,
    lines: [
        { productId: "p-1", productCode: "U00001", productName: "Pamuklu Tişört", variantId: null, expectedQty: 5, countedQty: null },
        { productId: "p-2", productCode: "U00002", productName: "Kot Pantolon", variantId: null, expectedQty: 3, countedQty: null },
    ],
    ...overrides,
});

function setup() {
    const calls = { lines: [] as unknown[], posted: 0 };
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["stok.read", "stok.sayim"] }))),
        http.get("*/api/erp/inventory/counts/c-1", () => HttpResponse.json(count())),
        http.get("*/api/erp/catalog/items", () => HttpResponse.json([sellable({ code: "U00001" })])),
        http.put("*/api/erp/inventory/counts/c-1/lines", async ({ request }) => {
            calls.lines.push(await request.json());
            return new HttpResponse(null, { status: 204 });
        }),
        http.post("*/api/erp/inventory/counts/c-1/post", () => {
            calls.posted += 1;
            return new HttpResponse(null, { status: 204 });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <CountSheet id="c-1" />
        </SessionProvider>,
    );
    return { ...view, calls };
}

describe("CountSheet", () => {
    it("CountSheet_BarcodeScanIncrements", async () => {
        const { user } = setup();
        const scan = await screen.findByRole("textbox", { name: "Barkod okut" });

        await user.type(scan, "8690001{Enter}");
        await waitFor(() => expect(screen.getByRole("textbox", { name: "U00001 sayılan" })).toHaveValue("1"));
        await user.type(scan, "8690001{Enter}");
        await waitFor(() => expect(screen.getByRole("textbox", { name: "U00001 sayılan" })).toHaveValue("2"));

        // Beklenen 5, sayılan 2: fark -3 ve satır vurgulu.
        expect(screen.getByText("-3")).toBeInTheDocument();
        expect(screen.getByText("U00001").closest("tr")).toHaveAttribute("data-diff", "true");
        expect(screen.getByRole("textbox", { name: "U00002 sayılan" })).toHaveValue("");
    });

    it("CountSheet_PostConfirms", async () => {
        const { user, calls } = setup();
        const field = await screen.findByRole("textbox", { name: "U00001 sayılan" });
        await user.type(field, "5");

        await user.click(screen.getByRole("button", { name: "Sayımı kapat" }));
        expect(await screen.findByText(/Kapatılan sayım geri alınamaz/)).toBeInTheDocument();
        expect(calls.posted).toBe(0);

        await user.click(screen.getByRole("button", { name: "Onayla ve kapat" }));
        await waitFor(() => expect(calls.posted).toBe(1));
        expect(calls.lines).toEqual([
            {
                lines: [
                    { productId: "p-1", variantId: null, countedQty: 5 },
                    { productId: "p-2", variantId: null, countedQty: null },
                ],
            },
        ]);
    });
});
