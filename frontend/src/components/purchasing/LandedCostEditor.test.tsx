import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import LandedCostEditor from "./LandedCostEditor";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const receipt = { id: "r-1", number: "MK2026000001", status: "posted", purchaseOrderId: null, purchaseOrderNumber: null, supplierId: "s-1", supplierName: "Akın Tekstil", branchId: "b-1", warehouseId: "w-1", date: "2026-03-05", currency: "TRY", total: 1000, lineCount: 3 };

const allocation = (n: number, allocatedTl: number) => ({
    receiptId: "r-1",
    receiptNumber: "MK2026000001",
    receiptLineId: `rl-${n}`,
    productId: `p-${n}`,
    productCode: `U0000${n}`,
    productName: `Kalem ${n}`,
    baseQuantity: 1,
    weightKgPerUnit: null,
    tlValue: 100,
    allocatedTl,
    byCostType: { navlun: allocatedTl },
});

function setup(modules: string[] = []) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read", "satinalma.manage", "fiyat.alis", "fatura.read"], modules }))),
        http.get("*/api/erp/purchasing/receipts", () => HttpResponse.json({ items: [receipt], total: 1 })),
        http.get("*/api/erp/invoices", () => HttpResponse.json({ items: [], total: 0, totals: { netTl: 0, vatTl: 0, payableTl: 0 } })),
    );
    return renderWithTheme(
        <SessionProvider>
            <LandedCostEditor />
        </SessionProvider>,
    );
}

async function pickReceiptAndAmount(user: ReturnType<typeof setup>["user"], amount: string) {
    await user.click(await screen.findByRole("combobox", { name: "Mal kabuller" }));
    await user.click(await screen.findByRole("option", { name: /MK2026000001/ }));
    await user.type(screen.getByRole("textbox", { name: "Tutar (TL)" }), amount);
}

beforeEach(() => resetNavigation("/satin-alma/masraf-dagitimi/yeni"));

describe("LandedCostEditor", () => {
    it("LandedCost_WeightMissingShowsProducts", async () => {
        server.use(
            http.post("*/api/erp/purchasing/landed-costs/preview", () =>
                HttpResponse.json({ status: 409, title: "Ağırlık anahtarı için kalem ağırlığı tanımlı olmalıdır. Ağırlığı eksik kalemler: U00001, U00002", code: "weight_missing" }, { status: 409 }),
            ),
        );
        const { user } = setup();
        await pickReceiptAndAmount(user, "100");
        await user.selectOptions(screen.getByRole("combobox", { name: "Dağıtım anahtarı" }), "weight");

        await user.click(screen.getByRole("button", { name: "Önizle" }));

        expect(await screen.findByText(/Ağırlığı eksik kalemler: U00001, U00002/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Kaydet ve işle" })).toBeDisabled();
    });

    it("LandedCost_PreviewSumsToTotal", async () => {
        let body: { key?: string; receiptIds?: string[]; sources?: { manualAmountTl: number }[] } = {};
        server.use(
            http.post("*/api/erp/purchasing/landed-costs/preview", async ({ request }) => {
                body = (await request.json()) as typeof body;
                return HttpResponse.json({
                    id: "00000000-0000-0000-0000-000000000000",
                    number: "",
                    status: "draft",
                    date: "2026-03-06",
                    key: "amount",
                    importFileId: null,
                    notes: null,
                    cancelReason: null,
                    totalTl: 100,
                    sources: [],
                    allocations: [allocation(1, 33.33), allocation(2, 33.33), allocation(3, 33.34)],
                    byCostType: { navlun: 100 },
                });
            }),
        );
        const { user } = setup();
        await pickReceiptAndAmount(user, "100");

        await user.click(screen.getByRole("button", { name: "Önizle" }));

        const total = await screen.findByTestId("allocation-total");
        expect(within(total).getByText("100,00 ₺")).toBeInTheDocument();
        expect(screen.getAllByText("33,34 ₺").length).toBeGreaterThan(0);
        expect(screen.queryByText(/dağıtım toplamı masraf toplamına eşit değil/i)).not.toBeInTheDocument();
        await waitFor(() => expect(body.receiptIds).toEqual(["r-1"]));
        expect(body.sources?.[0].manualAmountTl).toBe(100);
        expect(body.key).toBe("amount");
        expect(screen.getByRole("button", { name: "Kaydet ve işle" })).toBeEnabled();
    });
});
