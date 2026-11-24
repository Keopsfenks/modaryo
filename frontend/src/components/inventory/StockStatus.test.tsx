import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { productDto } from "@/components/catalog/form/fixtures";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import StockStatus from "./StockStatus";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const row = (overrides: Record<string, unknown> = {}) => ({
    productId: "p-1",
    productCode: "U00001",
    productName: "Pamuklu Tişört",
    variantId: null,
    variantCode: null,
    warehouseId: "w-1",
    warehouseName: "Merkez Depo",
    unit: "adet",
    physical: 25,
    reserved: 0,
    available: 25,
    minQty: null,
    avgCost: 12.5,
    ...overrides,
});

const warehouse = { id: "w-1", branchId: "b-1", code: "MRK", name: "Merkez Depo", kind: "normal", isDefault: true, isActive: true, inUse: true };

function setup(permissions: string[], rows = [row()]) {
    const queries: URLSearchParams[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([warehouse])),
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json([{ id: "c-1", name: "Giyim", parentId: null, sort: 0, productCount: 1, children: [] }])),
        http.get("*/api/erp/catalog/products/p-1", () =>
            HttpResponse.json(productDto({ units: [{ id: "u-1", unit: "koli", factor: 12, barcode: null }] })),
        ),
        http.get("*/api/erp/inventory/balances", ({ request }) => {
            queries.push(new URL(request.url).searchParams);
            return HttpResponse.json({ items: rows, total: rows.length });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <StockStatus />
        </SessionProvider>,
    );
    return { ...view, queries };
}

beforeEach(() => resetNavigation("/stok/durum"));

describe("StockStatus", () => {
    it("StockStatus_FiltersSent", async () => {
        const { user, queries } = setup(["stok.read"]);
        await screen.findByText("Pamuklu Tişört");

        await user.click(screen.getByRole("combobox", { name: "Depo" }));
        await user.click(await screen.findByRole("option", { name: "Merkez Depo" }));
        await user.click(screen.getByRole("combobox", { name: "Kategori" }));
        await user.click(await screen.findByRole("option", { name: "Giyim" }));
        await user.click(screen.getByRole("switch", { name: "Sadece stokta olanlar" }));
        await user.click(screen.getByRole("switch", { name: "Minimumun altında" }));

        await waitFor(() => {
            const last = queries.at(-1)!;
            expect(last.get("warehouseId")).toBe("w-1");
            expect(last.get("categoryId")).toBe("c-1");
            expect(last.get("onlyNonZero")).toBe("true");
            expect(last.get("belowMinimum")).toBe("true");
        });
    });

    it("StockStatus_ShowsLargestUnitBreakdown", async () => {
        setup(["stok.read"]);
        expect((await screen.findAllByText("25 adet (2 koli 1 adet)", { exact: false })).length).toBeGreaterThan(0);
    });

    it("StockStatus_HidesCostWithoutPermission", async () => {
        const { unmount } = setup(["stok.read"]);
        await screen.findByText("Pamuklu Tişört");
        expect(screen.queryByText("Ort. maliyet")).not.toBeInTheDocument();
        expect(screen.queryByText(/12,50/)).not.toBeInTheDocument();
        unmount();

        setup(["stok.read", "maliyet.read"]);
        expect(await screen.findByText("Ort. maliyet")).toBeInTheDocument();
        expect(await screen.findByText(/12,50/)).toBeInTheDocument();
    });
});
