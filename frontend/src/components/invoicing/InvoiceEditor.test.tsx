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

describe("Receipt-linked purchase draft", () => {
    it("InvoiceEditor_ReceiptLinkedDraft_LocksSupplierCurrencyAndStock", async () => {
        resetNavigation("/satin-alma/faturalar/inv-1");
        const line = {
            id: "l-1", sort: 1, productId: "p-1", variantId: null, description: "Kalem", unit: "adet", quantity: 10, unitPrice: 20, factor: 1, gross: 200, lineDiscount: 0, docDiscount: 0, net: 200,
            otv: 0, vatBase: 200, vat: 40, withheld: 0, total: 240, totalTl: 240, vatRate: 20, sourceLineId: "rl-1",
        };
        const totals = { gross: 200, discount: 0, net: 200, otv: 0, vat: 40, withheld: 0, payable: 240 };
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["fatura.read", "fatura.manage", "fiyat.alis", "cari.read", "stok.read"] }))),
            http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/invoices/codes", () => HttpResponse.json({ withholding: [], exemption: [] })),
            http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([])),
            http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [], total: 0 })),
            http.get("*/api/erp/current-accounts/acc-1", () => HttpResponse.json(accountDto({ id: "acc-1", name: "Tedarikçi AŞ", currency: "TRY" }))),
            http.get("*/api/erp/invoices/inv-1", () =>
                HttpResponse.json({
                    id: "inv-1", kind: "purchase", status: "draft", mode: "paper", scenario: null, branchId: "b-1", seriesId: null, number: null, externalNumber: "TED-1",
                    currentAccountId: "acc-1", currentAccountName: "Tedarikçi AŞ", date: "2026-03-10", currency: "TRY", rate: 1, rateOverridden: false, docDiscountPercent: null,
                    docDiscountAmount: null, totals, totalsTl: totals, affectsStock: false, warehouseId: null, paymentTermDays: 30, notes: null, returnOfInvoiceId: null,
                    cancelReason: null, version: 1, lines: [line], dueSchedule: [], vatSummary: [],
                }),
            ),
        );
        renderWithTheme(
            <SessionProvider>
                <InvoiceEditor group="purchase" id="inv-1" />
            </SessionProvider>,
        );

        expect(await screen.findByLabelText("Para birimi")).toBeDisabled();
        expect(screen.getByLabelText("Şube")).toBeDisabled();
        expect(screen.getByRole("combobox", { name: "Tedarikçi" })).toBeDisabled();
        expect(await screen.findByRole("switch", { name: "Stoğa işle" })).toBeDisabled();
    });
});
