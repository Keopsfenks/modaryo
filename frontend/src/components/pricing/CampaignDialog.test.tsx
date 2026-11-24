import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CampaignDialog from "./CampaignDialog";

function mockCatalog() {
    server.use(
        http.get("*/api/erp/catalog/categories", () => HttpResponse.json([{ id: "c1", name: "Kırtasiye", parentId: null, sort: 0, productCount: 1, children: [] }])),
        http.get("*/api/erp/catalog/products", () => HttpResponse.json({ items: [{ id: "p1", code: "U00001", name: "Kalem" }], total: 1 })),
    );
}

// Autocomplete aramaları ve yazma etkileşimleri yüklü makinede 5 sn sınırını aşabilir.
vi.setConfig({ testTimeout: 20000 });

describe("CampaignDialog", () => {
    it("CampaignDialog_BuyXPayYValidation", async () => {
        mockCatalog();
        let body: unknown;
        server.use(
            http.post("*/api/erp/pricing/campaigns", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json({}, { status: 201 });
            }),
        );
        const onSaved = vi.fn();
        const { user } = renderWithTheme(<CampaignDialog open onClose={vi.fn()} onSaved={onSaved} />);
        const dialog = await screen.findByRole("dialog");

        await user.type(within(dialog).getByLabelText("Kampanya adı"), "3 al 2 öde");
        await user.type(within(dialog).getByLabelText("Alınan adet (X)"), "3");
        await user.type(within(dialog).getByLabelText("Ödenen adet (Y)"), "3");
        await user.type(within(dialog).getByLabelText("Ürünler"), "Kalem");
        await user.click(await screen.findByRole("option", { name: "U00001 · Kalem" }, { timeout: 4000 }));
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(await within(dialog).findByText("X al Y öde için X, Y'den büyük ve Y en az 1 olmalıdır.")).toBeInTheDocument();
        expect(body).toBeUndefined();

        await user.clear(within(dialog).getByLabelText("Ödenen adet (Y)"));
        await user.type(within(dialog).getByLabelText("Ödenen adet (Y)"), "2");
        await user.click(within(dialog).getByRole("button", { name: "Pazartesi" }));
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(onSaved).toHaveBeenCalled());
        expect(body).toEqual({
            name: "3 al 2 öde",
            kind: "buyXPayY",
            buyQty: 3,
            payQty: 2,
            percent: null,
            validFrom: null,
            validTo: null,
            days: ["monday"],
            fromTime: null,
            toTime: null,
            productIds: ["p1"],
            variantIds: [],
            categoryIds: [],
            isActive: true,
        });
    });

    it("CampaignDialog_PercentOffNeedsScopeAndRange", async () => {
        mockCatalog();
        const { user } = renderWithTheme(<CampaignDialog open onClose={vi.fn()} onSaved={vi.fn()} />);
        const dialog = await screen.findByRole("dialog");

        await user.type(within(dialog).getByLabelText("Kampanya adı"), "Yaz indirimi");
        await user.click(within(dialog).getByRole("button", { name: "% indirim" }));
        await user.type(within(dialog).getByLabelText("İndirim yüzdesi (%)"), "120");
        await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

        expect(await within(dialog).findByText("İndirim yüzdesi 0'dan büyük, en çok 100 olmalıdır.")).toBeInTheDocument();
        expect(within(dialog).getByText("Kampanya için en az bir ürün, varyant ya da kategori seçin.")).toBeInTheDocument();
    });
});
