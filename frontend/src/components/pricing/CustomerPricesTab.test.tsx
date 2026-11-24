import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { accountDto, problem } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CustomerPricesTab from "./CustomerPricesTab";

const account = accountDto() as unknown as CurrentAccountDto;

const sellable = {
    productId: "p1",
    variantId: null,
    kind: "product",
    code: "U00001",
    name: "Kalem",
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

async function fillNewPrice(user: ReturnType<typeof renderWithTheme>["user"], price: string) {
    await user.click(await screen.findByRole("button", { name: "Özel fiyat ekle" }));
    await user.type(await screen.findByLabelText("Kalem"), "Kalem");
    await user.click(await screen.findByRole("option", { name: "U00001 · Kalem" }, { timeout: 4000 }));
    await user.type(screen.getByLabelText("1. kademe fiyatı"), price);
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
}

// Autocomplete aramaları ve yazma etkileşimleri yüklü makinede 5 sn sınırını aşabilir.
vi.setConfig({ testTimeout: 20000 });

describe("CustomerPricesTab", () => {
    it("CustomerPrices_OverlapShowsMessage", async () => {
        server.use(
            http.get("*/api/erp/pricing/customers/acc-1/prices", () => HttpResponse.json([])),
            http.get("*/api/erp/catalog/items", () => HttpResponse.json([sellable])),
            http.post("*/api/erp/pricing/customers/acc-1/prices", () => HttpResponse.json(problem(409, "Çakışma.", "price_overlap"), { status: 409 })),
        );
        const { user } = renderWithTheme(<CustomerPricesTab account={account} canManage />);

        await fillNewPrice(user, "80");

        expect(await screen.findByText("Bu kalem, birim ve para biriminde tarihleri çakışan bir özel fiyat zaten var.")).toBeInTheDocument();
    });

    it("CustomerPrices_CreateSendsTiersAndCurrency", async () => {
        let body: unknown;
        server.use(
            http.get("*/api/erp/pricing/customers/acc-1/prices", () => HttpResponse.json([])),
            http.get("*/api/erp/catalog/items", () => HttpResponse.json([sellable])),
            http.post("*/api/erp/pricing/customers/acc-1/prices", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json({}, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<CustomerPricesTab account={account} canManage />);

        await fillNewPrice(user, "80,5");

        await waitFor(() => expect(body).toBeDefined());
        expect(body).toEqual({
            productId: "p1",
            variantId: null,
            unit: "adet",
            currency: "TRY",
            vatIncluded: false,
            tiers: [{ minQty: 1, price: 80.5 }],
            validFrom: null,
            validTo: null,
        });
    });

    it("CustomerPrices_ReadOnlyHasNoAddButton", async () => {
        server.use(
            http.get("*/api/erp/pricing/customers/acc-1/prices", () =>
                HttpResponse.json([
                    {
                        id: "x1",
                        currentAccountId: "acc-1",
                        productId: "p1",
                        productCode: "U00001",
                        productName: "Kalem",
                        variantId: null,
                        variantCode: null,
                        unit: "adet",
                        currency: "TRY",
                        vatIncluded: false,
                        tiers: [
                            { minQty: 1, price: 80 },
                            { minQty: 10, price: 70 },
                        ],
                        validFrom: null,
                        validTo: "2026-12-31",
                    },
                ]),
            ),
        );
        renderWithTheme(<CustomerPricesTab account={account} canManage={false} />);

        expect(await screen.findByText("1+: 80,00 · 10+: 70,00 (KDV hariç)")).toBeInTheDocument();
        expect(screen.getByText("… – 31.12.2026")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Özel fiyat ekle" })).not.toBeInTheDocument();
    });
});
