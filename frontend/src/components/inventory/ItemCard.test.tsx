import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ItemCard from "./ItemCard";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const movement = (id: string, date: string, type: string, inQty: number, outQty: number, running: number, sourceType = "stock-document") => ({
    id,
    date,
    type,
    warehouseId: "w-1",
    sourceType,
    sourceId: `src-${id}`,
    in: inQty,
    out: outQty,
    running,
    unitCost: null,
    totalCost: null,
    note: null,
});

describe("ItemCard", () => {
    beforeEach(() => resetNavigation("/stok/hareketler", "urun=p-1&depo=w-1"));

    it("ItemCard_RunningQuantity", async () => {
        const queries: URLSearchParams[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["stok.read"] }))),
            http.get("*/api/erp/inventory/warehouses", () =>
                HttpResponse.json([{ id: "w-1", branchId: "b-1", code: "MRK", name: "Merkez Depo", kind: "normal", isDefault: true, isActive: true, inUse: true }]),
            ),
            http.get("*/api/erp/inventory/item-card", ({ request }) => {
                queries.push(new URL(request.url).searchParams);
                return HttpResponse.json({
                    productId: "p-1",
                    productCode: "U00001",
                    productName: "Pamuklu Tişört",
                    variantId: null,
                    unit: "adet",
                    openingQty: 4,
                    closingQty: 11,
                    movements: [
                        movement("m1", "2026-11-01", "receipt", 10, 0, 14),
                        movement("m2", "2026-11-02", "issue", 0, 3, 11, "stock-count"),
                    ],
                });
            }),
        );
        renderWithTheme(
            <SessionProvider>
                <ItemCard />
            </SessionProvider>,
        );

        await screen.findByText("U00001 · Pamuklu Tişört", {}, { timeout: 4000 });
        expect(queries[0].get("productId")).toBe("p-1");
        expect(queries[0].get("warehouseId")).toBe("w-1");

        const running = screen.getAllByTestId("running").map((c) => c.textContent);
        expect(running).toEqual(["4", "14", "11"]);

        const link = screen.getByRole("link", { name: "Stok fişi" });
        expect(link).toHaveAttribute("href", "/stok/fisler?belge=src-m1");
        expect(within(screen.getByRole("table")).getByRole("link", { name: "Sayım" })).toHaveAttribute("href", "/stok/sayim/src-m2");
        expect(screen.getByText("Kapanış (adet)")).toBeInTheDocument();
    });
});
