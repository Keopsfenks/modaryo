import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ProductList from "./ProductList";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const item = (overrides: Record<string, unknown> = {}) => ({
    id: "p-1",
    kind: "product",
    code: "U00001",
    name: "Pamuklu Tişört",
    categoryId: "c-1",
    categoryPath: "Giyim > Tişört",
    brandId: "b-1",
    brandName: "Modaryo",
    baseUnit: "adet",
    vatRate: 20,
    isActive: true,
    barcode: null,
    defaultSalePrice: { amount: 149.9, currency: "TRY", vatIncluded: true },
    variantCount: 0,
    tags: [],
    firstImageFileId: null,
    ...overrides,
});

const categories = [
    {
        id: "c-1",
        name: "Giyim",
        parentId: null,
        sort: 0,
        productCount: 1,
        children: [{ id: "c-2", name: "Tişört", parentId: "c-1", sort: 0, productCount: 1, children: [] }],
    },
];
const brands = [{ id: "b-1", name: "Modaryo", productCount: 1 }];

function setup(items: unknown[], permissions = ["katalog.read", "katalog.manage"]) {
    const queries: URLSearchParams[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json(categories)),
        http.get("*/api/erp/catalog/brands", () => HttpResponse.json(brands)),
        http.get("*/api/erp/catalog/products", ({ request }) => {
            queries.push(new URL(request.url).searchParams);
            return HttpResponse.json({ items, total: items.length });
        }),
    );
    return queries;
}

const render = (ui: React.ReactElement) => renderWithTheme(<SessionProvider>{ui}</SessionProvider>);

describe("ProductList", () => {
    beforeEach(() => resetNavigation("/stok/urunler"));

    it("ShowsRowsWithPrice", async () => {
        setup([item()]);

        render(<ProductList title="Ürünler" />);

        expect(await screen.findByText("Pamuklu Tişört")).toBeInTheDocument();
        expect(screen.getByText("149,90 ₺")).toBeInTheDocument();
        expect(screen.getByText("Giyim > Tişört")).toBeInTheDocument();
    });

    it("ProductList_FiltersSentToApi", async () => {
        const queries = setup([item()]);
        const { user } = render(<ProductList title="Ürünler" kind="product" />);
        await screen.findByText("Pamuklu Tişört");
        expect(queries[0].get("kind")).toBe("product");
        expect(queries[0].get("page")).toBe("1");
        await screen.findByRole("option", { name: /Tişört/ });

        await user.type(screen.getByLabelText(/ara/i), "ışık");
        await user.selectOptions(screen.getByLabelText("Kategori"), "c-2");
        await user.selectOptions(screen.getByLabelText("Marka"), "b-1");
        await user.type(screen.getByLabelText("Etiket"), "yaz");
        await user.click(screen.getByLabelText("Pasifleri göster"));

        await waitFor(() => {
            const last = queries[queries.length - 1];
            expect(last.get("search")).toBe("ışık");
            expect(last.get("categoryId")).toBe("c-2");
            expect(last.get("brandId")).toBe("b-1");
            expect(last.get("tag")).toBe("yaz");
            expect(last.get("includeInactive")).toBe("true");
            expect(last.get("page")).toBe("1");
        });
    });

    it("ProductList_ExportDownloadsXlsx", async () => {
        setup([item()]);
        let exportQuery: URLSearchParams | null = null;
        server.use(
            http.get("*/api/erp/catalog/products/export.xlsx", ({ request }) => {
                exportQuery = new URL(request.url).searchParams;
                return new HttpResponse(new Uint8Array([80, 75]), {
                    headers: { "Content-Type": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                });
            }),
        );
        const createObjectURL = vi.fn(() => "blob:test");
        const revokeObjectURL = vi.fn();
        Object.assign(URL, { createObjectURL, revokeObjectURL });
        const clicked: string[] = [];
        vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
            clicked.push(this.download);
        });
        const { user } = render(<ProductList title="Ürünler" kind="product" />);
        await screen.findByText("Pamuklu Tişört");

        await user.click(screen.getByRole("button", { name: "Dışa aktar" }));

        await waitFor(() => expect(clicked).toEqual(["urunler.xlsx"]));
        expect(createObjectURL).toHaveBeenCalled();
        expect(exportQuery!.get("kind")).toBe("product");
    });

    it("ProductList_HidesWriteButtonsWithoutManage", async () => {
        setup([item()], ["katalog.read"]);

        render(<ProductList title="Ürünler" />);

        await screen.findByText("Pamuklu Tişört");
        expect(screen.queryByRole("link", { name: /yeni ürün/i })).not.toBeInTheDocument();
        expect(screen.queryByRole("link", { name: /İçe aktar/ })).not.toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Dışa aktar" })).toBeInTheDocument();
    });

    it("ShowsWriteButtonsWithManage", async () => {
        setup([item()]);

        render(<ProductList title="Ürünler" />);

        expect(await screen.findByRole("link", { name: /yeni ürün/i })).toHaveAttribute("href", "/stok/urunler/yeni");
        expect(screen.getByRole("link", { name: /İçe aktar/ })).toHaveAttribute("href", "/stok/ice-aktar");
    });

    it("ServiceKind_UsesServiceNewLabel", async () => {
        setup([item({ kind: "service", baseUnit: "saat" })]);

        render(<ProductList title="Hizmetler" kind="service" />);

        expect(await screen.findByRole("link", { name: /yeni hizmet/i })).toHaveAttribute("href", "/stok/urunler/yeni?tur=hizmet");
    });

    it("RowClickNavigates", async () => {
        setup([item({ id: "abc" })]);
        const { user } = render(<ProductList title="Ürünler" />);

        await user.click(within((await screen.findByText("Pamuklu Tişört")).closest("tr")!).getByText("U00001"));

        expect(router.push).toHaveBeenCalledWith("/stok/urunler/abc");
    });
});
