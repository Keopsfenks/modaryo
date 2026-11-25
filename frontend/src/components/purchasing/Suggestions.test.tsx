import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import Suggestions from "./Suggestions";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const line = (code: string, qty: number) => ({ productId: `p-${code}`, productCode: code, productName: `Kalem ${code}`, variantId: null, warehouseId: "w-1", available: 2, onOrder: 0, minQty: 10, suggestedQty: qty, lastPrice: 12.5 });

const groups = [
    { supplierId: "s-1", supplierName: "Akın Tekstil", lines: [line("U1", 8), line("U2", 5)] },
    { supplierId: null, supplierName: "Tedarikçisiz", lines: [line("U3", 3)] },
];

beforeEach(() => resetNavigation("/satin-alma/oneriler"));

describe("Suggestions", () => {
    it("Suggestions_CreateDraftsNavigates", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read", "satinalma.manage", "fiyat.alis"] }))),
            http.get("*/api/erp/purchasing/suggestions", () => HttpResponse.json({ groups })),
        );
        let body: { groups: { supplierId: string; lines: { productId: string; quantity: number }[] }[] } | null = null;
        server.use(
            http.post("*/api/erp/purchasing/suggestions/create-orders", async ({ request }) => {
                body = (await request.json()) as typeof body;
                return HttpResponse.json([{ id: "po-9", number: "SA2026000009" }], { status: 201 });
            }),
        );

        const { user } = renderWithTheme(
            <SessionProvider>
                <Suggestions />
            </SessionProvider>,
        );

        const qty = await screen.findByRole("textbox", { name: "U1 miktar" });
        await user.clear(qty);
        await user.type(qty, "12");
        expect(screen.getByText(/Tedarikçisiz/)).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Sipariş taslakları oluştur" }));

        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/satin-alma/siparisler"));
        expect(body!.groups).toHaveLength(1);
        expect(body!.groups[0].supplierId).toBe("s-1");
        expect(body!.groups[0].lines.map((l) => [l.productId, l.quantity])).toEqual([
            ["p-U1", 12],
            ["p-U2", 5],
        ]);
    });
});
