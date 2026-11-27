import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PaymentForm from "./PaymentForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const cari = (overrides: Record<string, unknown> = {}) => ({
    id: "c-1", code: "C-0001", name: "Akın Tekstil", isCustomer: true, isSupplier: false, currency: "TRY", balance: 0, overLimit: false, overdueAmount: 0, isActive: true, ...overrides,
});
const account = (overrides: Record<string, unknown> = {}) => ({
    id: "a-1", kind: "cash", name: "Ana Kasa", currency: "TRY", branchId: "b-1", iban: null, bankName: null, balance: 100, isActive: true, ...overrides,
});

function setup(options: { cari?: Record<string, unknown>; accounts?: Record<string, unknown>[]; postResult?: Record<string, unknown>; posted?: unknown[] } = {}) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["odeme.manage", "tahsilat.manage"] }))),
        http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [cari(options.cari)], total: 1 })),
        http.get("*/api/erp/treasury/accounts", () => HttpResponse.json(options.accounts ?? [account()])),
        http.get("*/api/erp/treasury/open-items", () => HttpResponse.json([])),
        http.get("*/api/erp/exchange-rates/lookup", () =>
            HttpResponse.json({ currency: "USD", type: "forexBuying", rate: 32.5, effectiveDate: "2026-03-10", source: "tcmb", overridden: false }),
        ),
        http.post("*/api/erp/treasury/payments", async ({ request }) => {
            options.posted?.push(await request.json());
            return HttpResponse.json(options.postResult ?? { id: "d-1", number: "OD2026000001", warnings: [] }, { status: 201 });
        }),
    );
}

const render = () =>
    renderWithTheme(
        <SessionProvider>
            <PaymentForm kind="payment" />
        </SessionProvider>,
    );

type User = ReturnType<typeof render>["user"];

async function pickCari(user: User) {
    await user.click(await screen.findByRole("combobox", { name: /Tedarikçi/ }));
    await user.click(await screen.findByRole("option", { name: /Akın Tekstil/ }));
}

async function pickAccount(user: User, name: string) {
    await user.click(await screen.findByRole("combobox", { name: "Hesap" }));
    await user.click(await screen.findByRole("option", { name: new RegExp(name) }));
}

describe("PaymentForm", () => {
    beforeEach(() => resetNavigation("/finans/odeme/yeni"));

    it("PaymentForm_RateShownWhenCurrenciesDiffer", async () => {
        setup({ accounts: [account({ id: "a-2", name: "Dolar Kasa", currency: "USD" })] });
        const { user } = render();

        await pickCari(user);
        await pickAccount(user, "Dolar Kasa");

        expect(await screen.findByLabelText("Kur (USD/TL)")).toBeInTheDocument();
    });

    it("PaymentForm_RateHiddenWhenSameCurrency", async () => {
        setup();
        const { user } = render();

        await pickCari(user);
        await pickAccount(user, "Ana Kasa");

        expect(screen.queryByLabelText(/^Kur/)).not.toBeInTheDocument();
    });

    it("PaymentForm_NegativeCashWarningShown", async () => {
        const posted: unknown[] = [];
        setup({ posted, postResult: { id: "d-1", number: "OD2026000001", warnings: ["cash_negative"] } });
        const { user } = render();

        await pickCari(user);
        await pickAccount(user, "Ana Kasa");
        await user.type(screen.getByRole("textbox", { name: /^Tutar/ }), "500");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText(/Kasa bakiyesi eksiye düştü/)).toBeInTheDocument();
        expect(screen.getByText(/OD2026000001/)).toBeInTheDocument();
        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ currentAccountId: "c-1", accountId: "a-1", method: "cash", amount: 500, isAdvance: false });
    });

    it("CollectionForm_SalesOrderAdvancePrefill_PostsSalesOrderId", async () => {
        resetNavigation("/finans/tahsilat/yeni", "cari=c-1&avans=1&siparis=so-1&tutar=2453.33");
        const posted: unknown[] = [];
        setup({ posted });
        server.use(
            http.get("*/api/erp/current-accounts/c-1", () => HttpResponse.json({ ...cari(), kind: "corporate", addresses: [], hasMovements: false, defaultDueDays: 0, creditLimit: null })),
            http.post("*/api/erp/treasury/collections", async ({ request }) => {
                posted.push(await request.json());
                return HttpResponse.json({ id: "d-2", number: "TH2026000001", warnings: [] }, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <PaymentForm kind="collection" />
            </SessionProvider>,
        );

        await pickAccount(user, "Ana Kasa");
        expect(screen.getByRole("textbox", { name: /^Tutar/ })).toHaveValue("2453,33");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ currentAccountId: "c-1", amount: 2453.33, isAdvance: true, salesOrderId: "so-1" });
    });
});
