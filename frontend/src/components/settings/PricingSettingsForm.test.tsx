import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PricingSettingsForm from "./PricingSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const settings = { defaultSaleListId: "L1", defaultPurchaseListId: null, showVatIncluded: true, rounding: "none" };

const list = (id: string, name: string, kind: string) => ({
    id,
    name,
    kind,
    currency: "TRY",
    vatIncluded: false,
    validFrom: null,
    validTo: null,
    isDefault: false,
    supplierId: null,
    baseListId: null,
    adjustmentPercent: null,
    isActive: true,
    version: 1,
    itemCount: 0,
});

describe("PricingSettingsForm", () => {
    it("PricingSettings_RoundingOptions", async () => {
        const puts: { version: number; value: unknown }[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["*"] }))),
            http.get("*/api/erp/settings/fiyat", () => HttpResponse.json({ version: 2, value: settings })),
            http.put("*/api/erp/settings/fiyat", async ({ request }) => {
                const body = (await request.json()) as { version: number; value: unknown };
                puts.push(body);
                return HttpResponse.json({ version: 3, value: body.value });
            }),
            http.get("*/api/erp/pricing/lists", ({ request }) => {
                const kind = new URL(request.url).searchParams.get("kind");
                return HttpResponse.json(kind === "purchase" ? [list("P1", "Genel alış", "purchase")] : [list("L1", "Perakende", "sale")]);
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <PricingSettingsForm />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("combobox", { name: "Yuvarlama kuralı" }));
        const options = within(await screen.findByRole("listbox")).getAllByRole("option");
        expect(options.map((o) => o.textContent)).toEqual(["Yuvarlama yok", "0,01", "0,05", "0,10", "0,50", "1", "9 ile biten (örn. 99,90)"]);

        await user.click(within(screen.getByRole("listbox")).getByRole("option", { name: "0,05" }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Ayarlar kaydedildi.")).toBeInTheDocument();
        expect(puts).toEqual([{ version: 2, value: { ...settings, rounding: "fiveCents" } }]);
    });

    it("PricingSettings_PurchaseListDisabledWithoutAlis", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.fiyat", "fiyat.read"] }))),
            http.get("*/api/erp/settings/fiyat", () => HttpResponse.json({ version: 1, value: settings })),
            http.get("*/api/erp/pricing/lists", () => HttpResponse.json([list("L1", "Perakende", "sale")])),
        );
        renderWithTheme(
            <SessionProvider>
                <PricingSettingsForm />
            </SessionProvider>,
        );

        expect(await screen.findByRole("combobox", { name: "Varsayılan alış listesi" })).toHaveAttribute("aria-disabled", "true");
    });
});
