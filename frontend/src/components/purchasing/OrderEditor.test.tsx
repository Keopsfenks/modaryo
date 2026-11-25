import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SidebarContent from "@/components/layout/SidebarContent";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import OrderEditor from "./OrderEditor";
import { poDto, poLine } from "./purchasingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function session(permissions: string[]) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
        http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [], total: 0 })),
    );
}

beforeEach(() => resetNavigation("/satin-alma/siparisler/po-1"));

describe("OrderEditor", () => {
    it("OrderEditor_ShowsLastPurchaseHint", async () => {
        session(["satinalma.read", "satinalma.manage", "fiyat.alis"]);
        server.use(http.get("*/api/erp/purchasing/orders/po-1", () => HttpResponse.json(poDto())));

        renderWithTheme(
            <SessionProvider>
                <OrderEditor id="po-1" />
            </SessionProvider>,
        );

        expect(await screen.findByText(/Son alış: 12,50 ₺ \(10\.03\.2026\)/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Taslağı kaydet" })).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Onayla" })).toBeInTheDocument();
    });

    it("PricesHiddenWithoutAlis", async () => {
        session(["satinalma.read", "satinalma.receive"]);
        server.use(
            http.get("*/api/erp/purchasing/orders/po-1", () =>
                HttpResponse.json(
                    poDto({
                        status: "approved",
                        gross: null,
                        discount: null,
                        net: null,
                        vat: null,
                        total: null,
                        lines: [poLine({ unitPrice: null, net: null, vat: null, total: null, lastPurchase: null })],
                    }),
                ),
            ),
        );

        renderWithTheme(
            <SessionProvider>
                <OrderEditor id="po-1" />
            </SessionProvider>,
        );

        expect(await screen.findByText("SA2026000001")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Mal kabul yap" })).toHaveAttribute("href", "/satin-alma/mal-kabul/yeni?po=po-1");
        expect(screen.queryByText("Birim fiyat")).not.toBeInTheDocument();
        expect(screen.queryByText(/Toplam:/)).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Fatura oluştur" })).not.toBeInTheDocument();
    });

    it("OrderDetail_ApprovedOrder_ShowsActionsAndTabs", async () => {
        session(["satinalma.read", "satinalma.manage", "satinalma.receive", "fiyat.alis"]);
        server.use(http.get("*/api/erp/purchasing/orders/po-1", () => HttpResponse.json(poDto({ status: "approved" }))));

        const { user } = renderWithTheme(
            <SessionProvider>
                <OrderEditor id="po-1" />
            </SessionProvider>,
        );

        expect(await screen.findByRole("button", { name: "Kopyala" })).toBeInTheDocument();
        for (const name of ["PDF", "E-posta", "Kapat", "İptal"]) expect(screen.getByRole("button", { name })).toBeInTheDocument();
        await user.click(screen.getByRole("tab", { name: "Karşılaştırma" }));
        expect(await screen.findByText("Karşılaştırılacak satır yok.")).toBeInTheDocument();
    });
});

describe("Navigation", () => {
    it("QuarantineHiddenUnlessEnabled", async () => {
        resetNavigation("/panel");
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read"] }))),
            http.get("*/api/erp/settings/satinalma", () => HttpResponse.json({ version: 1, value: { quarantineEnabled: false } })),
        );
        renderWithTheme(
            <SessionProvider>
                <SidebarContent />
            </SessionProvider>,
        );

        expect(await screen.findByRole("link", { name: "Mal kabul" })).toHaveAttribute("href", "/satin-alma/mal-kabul");
        expect(screen.queryByRole("link", { name: "Karantina" })).not.toBeInTheDocument();
    });

    it("QuarantineVisibleWhenEnabled", async () => {
        resetNavigation("/panel");
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satinalma.read"] }))),
            http.get("*/api/erp/settings/satinalma", () => HttpResponse.json({ version: 1, value: { quarantineEnabled: true } })),
        );
        renderWithTheme(
            <SessionProvider>
                <SidebarContent />
            </SessionProvider>,
        );

        expect(await screen.findByRole("link", { name: "Karantina" })).toHaveAttribute("href", "/satin-alma/karantina");
    });
});
