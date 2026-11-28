import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { ShippingSettings } from "@/lib/api/shipping";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ShippingSettingsForm from "./ShippingSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const value: ShippingSettings = {
    usePickPack: true,
    notifyCustomerOnShip: true,
    carriers: [
        { code: "yurtici", name: "Yurtiçi", trackingUrlTemplate: "https://kargo.example/yurtici?code={no}", isActive: true },
        { code: "diger", name: "Diğer", trackingUrlTemplate: null, isActive: true },
    ],
    shipEmailSubject: "Kargoya verildi ({belgeNo})",
    shipEmailBody: "Sayın {alici}, takip: {takipLink}",
};

function setup() {
    const puts: { version: number; value: ShippingSettings }[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.sevkiyat"] }))),
        http.get("*/api/erp/settings/sevkiyat", () => HttpResponse.json({ version: 4, value })),
        http.put("*/api/erp/settings/sevkiyat", async ({ request }) => {
            const body = (await request.json()) as (typeof puts)[number];
            puts.push(body);
            return HttpResponse.json({ version: 5, value: body.value });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <ShippingSettingsForm />
        </SessionProvider>,
    );
    return { ...view, puts };
}

describe("ShippingSettingsForm", () => {
    it("Settings_TrackingTemplateNeedsNoPlaceholder", async () => {
        const { user, puts } = setup();

        const template = await screen.findByRole("textbox", { name: "Yurtiçi takip bağlantısı" });
        await user.clear(template);
        await user.type(template, "https://kargo.example/yurtici");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        // Şablonda {no} yoksa kayıt gitmez ve alan hata gösterir.
        expect(await screen.findByText("Takip bağlantısı https ile başlamalı ve {no} içermelidir.")).toBeInTheDocument();
        expect(puts).toHaveLength(0);
    });

    it("Settings_SavesWithVersion", async () => {
        const { user, puts } = setup();

        await user.click(await screen.findByRole("switch", { name: "Toplama ve paketleme akışını kullan" }));
        await user.click(screen.getByRole("switch", { name: "Diğer etkin" }));
        const template = screen.getByRole("textbox", { name: "Yurtiçi takip bağlantısı" });
        await user.clear(template);
        await user.type(template, "https://yeni.example/q?no=");
        await user.type(template, "{{no}");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].version).toBe(4);
        expect(puts[0].value.usePickPack).toBe(false);
        expect(puts[0].value.carriers.find((c) => c.code === "diger")?.isActive).toBe(false);
        expect(puts[0].value.carriers.find((c) => c.code === "yurtici")?.trackingUrlTemplate).toBe("https://yeni.example/q?no={no}");
    });
});
