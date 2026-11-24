import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PriceItemDto, PriceListDto } from "@/lib/api/pricing";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PriceListEditor from "./PriceListEditor";
import PriceLists from "./PriceLists";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const listDto = (overrides: Partial<PriceListDto> = {}): PriceListDto => ({
    id: "L1",
    name: "Perakende",
    kind: "sale",
    currency: "TRY",
    vatIncluded: false,
    validFrom: null,
    validTo: null,
    isDefault: true,
    supplierId: null,
    baseListId: null,
    adjustmentPercent: null,
    isActive: true,
    version: 1,
    itemCount: 1,
    ...overrides,
});

const itemDto = (overrides: Partial<PriceItemDto> = {}): PriceItemDto => ({
    id: "i1",
    productId: "p1",
    productCode: "U00001",
    productName: "Kalem",
    variantId: null,
    variantCode: null,
    unit: "adet",
    tiers: [{ minQty: 1, price: 100 }],
    version: 3,
    derived: false,
    ...overrides,
});

const sellable = {
    productId: "p2",
    variantId: null,
    kind: "product",
    code: "U00002",
    name: "Defter",
    baseUnit: "adet",
    vatRate: 20,
    soldByMeasure: false,
    measureUnit: null,
    tracksSerialLot: false,
    salePrice: null,
    purchasePrice: null,
    units: [],
    isActive: true,
};

function setup(items: PriceItemDto[], permissions: string[] = ["*"], list: PriceListDto = listDto()) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(`*/api/erp/pricing/lists/${list.id}`, () => HttpResponse.json(list)),
        http.get(`*/api/erp/pricing/lists/${list.id}/items`, () => HttpResponse.json({ items, total: items.length })),
        http.get("*/api/erp/catalog/items", () => HttpResponse.json([sellable])),
    );
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

// Autocomplete aramaları ve yazma etkileşimleri yüklü makinede 5 sn sınırını aşabilir.
vi.setConfig({ testTimeout: 20000 });

describe("PriceListEditor", () => {
    beforeEach(() => resetNavigation("/satis/fiyat-listeleri/L1"));

    it("Editor_AddItemSinglePrice_Saves", async () => {
        setup([itemDto()]);
        let body: unknown;
        server.use(
            http.put("*/api/erp/pricing/lists/L1/items", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json({ rows: [] });
            }),
        );
        const { user } = render(<PriceListEditor id="L1" />);

        await screen.findByText("U00001 · Kalem");
        await user.type(screen.getByLabelText("Kalem ekle"), "Defter");
        await user.click(await screen.findByRole("option", { name: "U00002 · Defter" }, { timeout: 4000 }));

        await user.type(await screen.findByLabelText("U00002 fiyatı"), "49,9");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("1 satır kaydedildi.")).toBeInTheDocument();
        expect(body).toEqual({ rows: [{ productId: "p2", variantId: null, unit: "adet", tiers: [{ minQty: 1, price: 49.9 }] }] });
    });

    it("Editor_InvalidPrice_DoesNotSave", async () => {
        setup([itemDto()]);
        let called = false;
        server.use(
            http.put("*/api/erp/pricing/lists/L1/items", () => {
                called = true;
                return HttpResponse.json({ rows: [] });
            }),
        );
        const { user } = render(<PriceListEditor id="L1" />);

        const price = await screen.findByLabelText("U00001 fiyatı");
        await user.clear(price);
        await user.type(price, "abc");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText(/satırda hata var/)).toBeInTheDocument();
        expect(called).toBe(false);
    });

    it("Editor_DerivedRowGreyUntilExplicit", async () => {
        setup([itemDto({ id: null, derived: true, version: 0, tiers: [{ minQty: 1, price: 90 }] })]);
        let body: { rows: { version?: number }[] } | undefined;
        server.use(
            http.put("*/api/erp/pricing/lists/L1/items", async ({ request }) => {
                body = (await request.json()) as typeof body;
                return HttpResponse.json({ rows: [] });
            }),
        );
        const { user } = render(<PriceListEditor id="L1" />);

        expect(await screen.findByTestId("derived-U00001")).toHaveTextContent("90,00");
        expect(screen.queryByLabelText("U00001 fiyatı")).not.toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "Elle fiyat ver" }));

        const input = await screen.findByLabelText("U00001 fiyatı");
        expect(input).toHaveValue("90");
        expect(screen.queryByTestId("derived-U00001")).not.toBeInTheDocument();

        await user.clear(input);
        await user.type(input, "85");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(body).toBeDefined());
        expect(body!.rows[0]).not.toHaveProperty("version");
    });

    it("Editor_StaleRowConflict_HighlightsRow", async () => {
        const items: PriceItemDto[] = [itemDto({ version: 3 })];
        setup(items);
        server.use(http.put("*/api/erp/pricing/lists/L1/items", () => HttpResponse.json(problem(409, "Kayıt başkası tarafından değiştirildi.", "version_conflict"), { status: 409 })));
        const { user } = render(<PriceListEditor id="L1" />);

        const price = await screen.findByLabelText("U00001 fiyatı");
        await user.clear(price);
        await user.type(price, "120");

        // Kaydetmeden önce başka biri satırı güncelledi.
        server.use(http.get("*/api/erp/pricing/lists/L1/items", () => HttpResponse.json({ items: [itemDto({ version: 4, tiers: [{ minQty: 1, price: 110 }] })], total: 1 })));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Bu satır başka biri tarafından değiştirildi.")).toBeInTheDocument();
        // Girilen değer korunur.
        expect(screen.getByLabelText("U00001 fiyatı")).toHaveValue("120");
    });

    it("BulkAdjust_SendsPercentAndCategory", async () => {
        setup([itemDto()]);
        let body: unknown;
        server.use(
            http.get("*/api/erp/catalog/categories", () => HttpResponse.json([{ id: "c1", name: "Kırtasiye", parentId: null, sort: 0, productCount: 1, children: [] }])),
            http.get("*/api/erp/catalog/brands", () => HttpResponse.json([])),
            http.post("*/api/erp/pricing/lists/L1/bulk-adjust", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json({ updated: 4 });
            }),
        );
        const { user } = render(<PriceListEditor id="L1" />);

        await user.click(await screen.findByRole("button", { name: "Toplu fiyat ayarı" }));
        const dialog = await screen.findByRole("dialog");
        await user.type(within(dialog).getByLabelText("Yüzde (%)"), "10");
        await user.click(within(dialog).getByLabelText("Kategori"));
        await user.click(await screen.findByRole("option", { name: "Kırtasiye" }, { timeout: 4000 }));
        await user.click(within(dialog).getByRole("button", { name: "Uygula" }));

        expect(await screen.findByText("4 satır güncellendi.")).toBeInTheDocument();
        expect(body).toEqual({ percent: 10, categoryId: "c1", brandId: null });
    });

    it("PurchaseListsHiddenWithoutAlis", async () => {
        let listCalled = false;
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fiyat.read", "fiyat.manage"] }))),
            http.get("*/api/erp/pricing/lists", () => {
                listCalled = true;
                return HttpResponse.json([]);
            }),
        );
        render(<PriceLists kind="purchase" title="Alış fiyatları" />);

        expect(await screen.findByText("Alış fiyat listelerini görmek için yetkiniz yok.")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Yeni liste" })).not.toBeInTheDocument();
        expect(listCalled).toBe(false);
    });

    it("PriceLists_ShowsDerivedAndDefault", async () => {
        const base = listDto();
        const derived = listDto({ id: "L2", name: "Bayi", isDefault: false, baseListId: "L1", adjustmentPercent: -10 });
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fiyat.read"] }))),
            http.get("*/api/erp/pricing/lists", () => HttpResponse.json([base, derived])),
        );
        render(<PriceLists kind="sale" title="Fiyat listeleri" />);

        expect(await screen.findByText("Türetilmiş (Perakende −%10)")).toBeInTheDocument();
        expect(screen.getByText("Varsayılan")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Yeni liste" })).not.toBeInTheDocument();
    });
});
