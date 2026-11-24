import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SidebarContent from "@/components/layout/SidebarContent";
import { SessionProvider } from "@/session/SessionProvider";
import { accountDto, sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import InvoiceEditor from "./InvoiceEditor";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/satis/faturalar/yeni"));

describe("InvoiceEditor", () => {
    it("InvoiceEditor_PicksCariFillsTermAndCurrency", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fatura.read", "fatura.manage", "cari.read"] }))),
            http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/invoices/codes", () => HttpResponse.json({ withholding: [], exemption: [] })),
            http.get("*/api/erp/current-accounts", () =>
                HttpResponse.json({
                    items: [{ id: "acc-usd", code: "C-0002", name: "Dolar Ltd", isCustomer: true, isSupplier: false, currency: "USD", balance: 0, overLimit: false, overdueAmount: 0, isActive: true }],
                    total: 1,
                }),
            ),
            http.get("*/api/erp/current-accounts/acc-usd", () => HttpResponse.json(accountDto({ id: "acc-usd", name: "Dolar Ltd", currency: "USD", defaultDueDays: 45 }))),
            http.get("*/api/erp/exchange-rates/lookup", () => HttpResponse.json({ currency: "USD", type: "forexBuying", rate: 32.5, effectiveDate: "2026-03-10", source: "tcmb", overridden: false })),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <InvoiceEditor group="sales" />
            </SessionProvider>,
        );

        const combo = await screen.findByRole("combobox", { name: "Müşteri" });
        await user.click(combo);
        await user.click(await screen.findByRole("option", { name: /Dolar Ltd/ }, { timeout: 4000 }));

        expect(await screen.findByLabelText("Vade günü")).toHaveValue("45");
        expect(screen.getByLabelText("Para birimi")).toHaveValue("USD");
        expect(await screen.findByLabelText("Kur (USD/TL)")).toBeInTheDocument();
    });

    it("InvoiceEditor_PurchaseWithoutAlisPermission_ShowsNoAccess", async () => {
        resetNavigation("/satin-alma/faturalar/yeni");
        server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fatura.read", "fatura.manage"] }))));
        renderWithTheme(
            <SessionProvider>
                <InvoiceEditor group="purchase" />
            </SessionProvider>,
        );

        expect(await screen.findByText("Faturayı görüntülemek için yetkiniz yok.")).toBeInTheDocument();
    });
});

describe("Navigation", () => {
    it("PurchaseInvoicesHiddenWithoutAlis", async () => {
        resetNavigation("/panel");
        server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fatura.read"] }))));
        renderWithTheme(
            <SessionProvider>
                <SidebarContent />
            </SessionProvider>,
        );

        expect(await screen.findByRole("link", { name: "Faturalar" })).toHaveAttribute("href", "/satis/faturalar");
        expect(screen.queryByRole("link", { name: "Alış faturaları" })).not.toBeInTheDocument();
    });

    it("PurchaseInvoicesVisibleWithAlis", async () => {
        resetNavigation("/panel");
        server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fatura.read", "fiyat.alis"] }))));
        renderWithTheme(
            <SessionProvider>
                <SidebarContent />
            </SessionProvider>,
        );

        expect(await screen.findByRole("link", { name: "Alış faturaları" })).toHaveAttribute("href", "/satin-alma/faturalar");
    });
});
