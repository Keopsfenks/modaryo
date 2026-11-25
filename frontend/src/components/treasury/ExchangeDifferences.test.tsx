import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ExchangeDifferences from "./ExchangeDifferences";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const diff = (overrides: Record<string, unknown> = {}) => ({
    currentAccountId: "c-1", currentAccountName: "Akın Tekstil", side: "receivable", allocationIds: ["al-1"], invoiceNumber: "SF2026000001",
    foreignAmount: 100, currency: "USD", invoiceRate: 32, paymentRate: 32.5, difference: 50, ...overrides,
});

function setup(rows: unknown[], posted: unknown[] = []) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["*"] }))),
        http.get("*/api/erp/treasury/exchange-differences", () => HttpResponse.json(rows)),
        http.get("*/api/erp/treasury/accounts", () => HttpResponse.json([])),
        http.post("*/api/erp/treasury/exchange-differences/invoice", async ({ request }) => {
            posted.push(await request.json());
            return HttpResponse.json({ invoiceId: "inv-9", amount: 50, vatRate: 20 }, { status: 201 });
        }),
    );
}

const render = () =>
    renderWithTheme(
        <SessionProvider>
            <ExchangeDifferences />
        </SessionProvider>,
    );

describe("ExchangeDifferences", () => {
    beforeEach(() => resetNavigation("/finans/kur-farki"));

    it("ExchangeDifferences_CreateInvoiceNavigatesToDraft", async () => {
        const posted: unknown[] = [];
        setup([diff()], posted);
        const { user } = render();

        await user.click(await screen.findByRole("checkbox", { name: "SF2026000001 seç" }));
        await user.click(screen.getByRole("button", { name: "Kur farkı faturası oluştur" }));
        await user.click(await screen.findByRole("button", { name: "Faturayı oluştur" }));

        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/satis/faturalar/inv-9"));
        expect(posted).toEqual([{ allocationIds: ["al-1"], vatRate: 20 }]);
    });

    it("ExchangeDifferences_NegativeOffersExpenseOnly", async () => {
        setup([diff({ difference: -50, allocationIds: ["al-2"] })]);
        const { user } = render();

        await user.click(await screen.findByRole("checkbox", { name: "SF2026000001 seç" }));

        expect(screen.getByRole("button", { name: "Kur farkı faturası oluştur" })).toBeDisabled();
        expect(screen.getByRole("button", { name: "Kur farkı gideri oluştur" })).toBeEnabled();
    });

    it("ExchangeDifferences_PayablePositiveOffersIncomeOnly_WithoutAccountPicker", async () => {
        const posted: unknown[] = [];
        setup([diff({ side: "payable", difference: 50, allocationIds: ["al-3"] })]);
        server.use(
            http.post("*/api/erp/treasury/exchange-differences/expense", async ({ request }) => {
                posted.push(await request.json());
                return HttpResponse.json({ expenseId: "gf-1", number: "GF2026000001", amount: 50, kind: "Income" }, { status: 201 });
            }),
        );
        const { user } = render();

        await user.click(await screen.findByRole("checkbox", { name: "SF2026000001 seç" }));

        // Tedarikçi tarafındaki pozitif fark için satış faturası kesilmez; gelir fişi girilir ve hesap seçimi yoktur.
        expect(screen.getByRole("button", { name: "Kur farkı faturası oluştur" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "Kur farkı geliri oluştur" }));
        expect(screen.queryByLabelText(/Ödeme hesabı/)).not.toBeInTheDocument();
        await user.click(await screen.findByRole("button", { name: "Geliri oluştur" }));

        expect(await screen.findByText(/Kur farkı gelir fişi oluşturuldu: GF2026000001/)).toBeInTheDocument();
        expect(posted).toEqual([{ allocationIds: ["al-3"] }]);
    });
});
