import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { PosSettings } from "@/lib/api/pos";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PosSettingsForm from "./PosSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const value: PosSettings = { legalDocumentMode: "fiscalReceipt", retailCustomerId: "r1", scaleBarcodes: [], allowNegativeCashWarningOnly: true, requireCustomerForVeresiye: true };

function setup(ebelgeEnabled: boolean) {
    const puts: { version: number; value: PosSettings }[] = [];
    server.use(
        http.get("http://localhost:3000/api/identity/auth/session", () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.pos"] }))),
        http.get("*/api/erp/settings/pos", () => HttpResponse.json({ version: 2, value })),
        http.get("*/api/erp/settings/ebelge", () => HttpResponse.json({ version: 1, value: { enabled: ebelgeEnabled, defaultScenario: "basic", eIrsaliyeEnabled: false } })),
        http.put("*/api/erp/settings/pos", async ({ request }) => {
            const body = (await request.json()) as (typeof puts)[number];
            puts.push(body);
            return HttpResponse.json({ version: 3, value: body.value });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <PosSettingsForm />
        </SessionProvider>,
    );
    return { ...view, puts };
}

describe("PosSettingsForm", () => {
    it("ModeEArchiveDisabledWithoutEBelge", async () => {
        setup(false);

        const earsiv = await screen.findByRole("radio", { name: /Yazar kasam yok/ });
        await waitFor(() => expect(earsiv).toBeDisabled());
        expect(screen.getByRole("radio", { name: /Yazar kasam var/ })).toBeChecked();
        expect(screen.getByText(/e-Belge ayarlarında etkinleştirilmelidir/)).toBeInTheDocument();
    });

    it("Mode_EArchiveSavedWhenEBelgeEnabled", async () => {
        const { user, puts } = setup(true);

        const earsiv = await screen.findByRole("radio", { name: /Yazar kasam yok/ });
        await waitFor(() => expect(earsiv).toBeEnabled());
        await user.click(earsiv);
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].version).toBe(2);
        expect(puts[0].value.legalDocumentMode).toBe("eArchiveInvoice");
    });
});
