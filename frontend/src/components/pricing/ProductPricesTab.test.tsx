import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { productDto } from "@/components/catalog/form/fixtures";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ProductPricesTab from "./ProductPricesTab";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const list = (id: string, name: string) => ({
    id,
    name,
    kind: "sale",
    currency: "TRY",
    vatIncluded: false,
    validFrom: null,
    validTo: null,
    isDefault: id === "L1",
    supplierId: null,
    baseListId: null,
    adjustmentPercent: null,
    isActive: true,
    version: 1,
    itemCount: 1,
});

const row = (overrides: Record<string, unknown>) => ({
    id: "i1",
    productId: "p-1",
    productCode: "U00001",
    productName: "Pamuklu Tişört",
    variantId: null,
    variantCode: null,
    unit: "adet",
    version: 1,
    derived: false,
    ...overrides,
});

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

function setup() {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fiyat.read"] }))),
        http.get("*/api/erp/pricing/lists", () => HttpResponse.json([list("L1", "Perakende"), list("L2", "Bayi")])),
        http.get("*/api/erp/pricing/lists/L1/items", () =>
            HttpResponse.json({
                items: [
                    row({
                        tiers: [
                            { minQty: 1, price: 100 },
                            { minQty: 10, price: 90 },
                        ],
                    }),
                ],
                total: 1,
            }),
        ),
        http.get("*/api/erp/pricing/lists/L2/items", () =>
            HttpResponse.json({ items: [row({ id: null, derived: true, version: 0, tiers: [{ minQty: 1, price: 85 }] })], total: 1 }),
        ),
        http.get("*/api/erp/pricing/history", () =>
            HttpResponse.json({
                items: [
                    {
                        id: "h1",
                        at: "2026-11-20T10:00:00Z",
                        actorLabel: "Ali Veli",
                        scope: "list",
                        scopeId: "L1",
                        productId: "p-1",
                        productCode: "U00001",
                        variantId: null,
                        unit: "adet",
                        before: [{ minQty: 1, price: 95 }],
                        after: [{ minQty: 1, price: 100 }],
                        kind: "sale",
                    },
                ],
                total: 1,
            }),
        ),
        http.get("*/api/erp/current-accounts", () =>
            HttpResponse.json({
                items: [{ id: "acc-1", code: "C-0001", name: "Akın Tekstil", isCustomer: true, isSupplier: false, currency: "TRY", balance: 0, overLimit: false, overdueAmount: 0, isActive: true }],
                total: 1,
            }),
        ),
    );
}

// Autocomplete aramaları ve yazma etkileşimleri yüklü makinede 5 sn sınırını aşabilir.
vi.setConfig({ testTimeout: 20000 });

describe("ProductPricesTab", () => {
    it("ProductPrices_QuoteTesterShowsSource", async () => {
        setup();
        let query: URLSearchParams | undefined;
        server.use(
            http.get("*/api/erp/pricing/quote", ({ request }) => {
                query = new URL(request.url).searchParams;
                return HttpResponse.json({ unitPrice: 85, currency: "TRY", vatIncluded: false, source: "groupList", sourceId: "L2", tierMinQty: 1 });
            }),
        );
        const { user } = render(<ProductPricesTab product={productDto()} />);

        await user.type(await screen.findByLabelText(/Cari \(müşteri\)/), "Akın");
        await user.click(await screen.findByRole("option", { name: "C-0001 · Akın Tekstil" }, { timeout: 4000 }));
        await user.click(screen.getByRole("button", { name: "Sorgula" }));

        expect(await screen.findByText("Kaynak: Müşteri grubu listesi: Bayi")).toBeInTheDocument();
        expect(query?.get("currentAccountId")).toBe("acc-1");
        expect(query?.get("productId")).toBe("p-1");
        expect(query?.get("unit")).toBe("adet");
        expect(query?.get("quantity")).toBe("1");
    });

    it("ProductPrices_ListsAndHistory", async () => {
        setup();
        render(<ProductPricesTab product={productDto()} />);

        expect(await screen.findByText("1+: 100,00 · 10+: 90,00")).toBeInTheDocument();
        expect(screen.getByText("Türetilmiş")).toBeInTheDocument();
        expect(await screen.findByText("Ali Veli")).toBeInTheDocument();
        expect(screen.getByText("95,00")).toBeInTheDocument();
    });

    it("ProductPrices_InvalidQuantityBlocksQuery", async () => {
        setup();
        let called = false;
        server.use(
            http.get("*/api/erp/pricing/quote", () => {
                called = true;
                return HttpResponse.json({});
            }),
        );
        const { user } = render(<ProductPricesTab product={productDto()} />);

        const quantity = await screen.findByLabelText("Miktar");
        await user.clear(quantity);
        await user.type(quantity, "0");
        await user.click(screen.getByRole("button", { name: "Sorgula" }));

        expect(await screen.findByText("Miktar sıfırdan büyük bir sayı olmalı.")).toBeInTheDocument();
        expect(called).toBe(false);
    });
});
