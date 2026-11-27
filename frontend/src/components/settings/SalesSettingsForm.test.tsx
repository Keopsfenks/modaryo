import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SalesSettingsForm from "./SalesSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

type Saved = { version: number; value: { quoteValidityDays: number; requireSalesRep: boolean; defaultInstallmentCount: number; prepaymentDefaultOff: boolean } };

describe("SalesSettingsForm", () => {
    it("SalesSettings_SavesWithVersion", async () => {
        let put: Saved | null = null;
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.satis"] }))),
            http.get("*/api/erp/settings/satis", () => HttpResponse.json({ version: 2, value: { quoteValidityDays: 15, requireSalesRep: false, defaultInstallmentCount: 3, prepaymentDefaultOff: true } })),
            http.put("*/api/erp/settings/satis", async ({ request }) => {
                put = (await request.json()) as Saved;
                return HttpResponse.json({ version: 3, value: put.value });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <SalesSettingsForm />
            </SessionProvider>,
        );

        const days = await screen.findByRole("textbox", { name: "Teklif geçerlilik süresi (gün)" });
        await user.clear(days);
        await user.type(days, "30");
        await user.click(screen.getByRole("switch", { name: "Teklif ve siparişte satış temsilcisi zorunlu" }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(put).not.toBeNull());
        expect(put).toMatchObject({ version: 2, value: { quoteValidityDays: 30, requireSalesRep: true, defaultInstallmentCount: 3 } });
    });

    it("SalesSettings_InvalidInstallmentCountBlocksSave", async () => {
        let puts = 0;
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.satis"] }))),
            http.get("*/api/erp/settings/satis", () => HttpResponse.json({ version: 2, value: { quoteValidityDays: 15, requireSalesRep: false, defaultInstallmentCount: 3, prepaymentDefaultOff: true } })),
            http.put("*/api/erp/settings/satis", () => {
                puts++;
                return HttpResponse.json({});
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <SalesSettingsForm />
            </SessionProvider>,
        );

        const count = await screen.findByRole("textbox", { name: "Varsayılan taksit sayısı" });
        await user.clear(count);
        await user.type(count, "40");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("1 ile 36 arasında bir taksit sayısı girin.")).toBeInTheDocument();
        expect(puts).toBe(0);
    });
});
