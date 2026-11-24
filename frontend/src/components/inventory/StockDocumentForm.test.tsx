import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sellable, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import StockDocumentForm from "./StockDocumentForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const warehouse = { id: "w-1", branchId: "b-1", code: "MRK", name: "Merkez Depo", kind: "normal", isDefault: true, isActive: true, inUse: false };

beforeEach(() => resetNavigation("/stok/fisler/yeni"));

describe("StockDocumentForm", () => {
    it("StockDocument_InsufficientStock_ShowsLineError", async () => {
        const posts: Record<string, unknown>[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["stok.read", "stok.manage"] }))),
            http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([warehouse])),
            http.get("*/api/erp/catalog/items", () => HttpResponse.json([sellable()])),
            http.post("*/api/erp/inventory/documents", async ({ request }) => {
                posts.push((await request.json()) as Record<string, unknown>);
                return HttpResponse.json(problem(409, "Yetersiz stok: U00001 - Pamuklu Tişört.", "insufficient_stock"), { status: 409 });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <StockDocumentForm />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("combobox", { name: "Fiş türü" }));
        await user.click(await screen.findByRole("option", { name: "Çıkış fişi" }));
        await user.type(screen.getByRole("combobox", { name: "Kalem ekle" }), "U0000");
        await user.click(await screen.findByRole("option", { name: /U00001/ }, { timeout: 4000 }));
        await user.clear(screen.getByRole("textbox", { name: "U00001 miktar" }));
        await user.type(screen.getByRole("textbox", { name: "U00001 miktar" }), "5");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        const row = (await screen.findByText("Yetersiz stok: U00001 - Pamuklu Tişört.", { selector: "[role=alert]" })).closest("tr");
        expect(row).not.toBeNull();
        expect(within(row as HTMLElement).getByText("U00001 · Pamuklu Tişört")).toBeInTheDocument();
        expect(posts[0]).toMatchObject({ kind: "issue", warehouseId: "w-1", lines: [{ productId: "p-1", quantity: 5, unit: "adet" }] });
        expect(router.push).not.toHaveBeenCalled();
    });
});
