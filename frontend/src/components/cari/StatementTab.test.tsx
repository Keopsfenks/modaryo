import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { SessionProvider } from "@/session/SessionProvider";
import { accountDto, sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import StatementTab from "./StatementTab";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const row = (overrides: Record<string, unknown> = {}) => ({
    id: "e1",
    date: "2026-03-05",
    type: "debitNote",
    documentNo: "F-100",
    description: "Kumaş satışı",
    debit: 1500,
    credit: 0,
    dueDate: "2026-04-04",
    runningBalance: 1500,
    cancelled: false,
    cancelReason: null,
    ...overrides,
});

function setup(rows: unknown[]) {
    const calls: URLSearchParams[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo())),
        http.get("*/api/erp/current-accounts/acc-1/statement", ({ request }) => {
            calls.push(new URL(request.url).searchParams);
            return HttpResponse.json({
                currency: "TRY",
                openingBalance: 200,
                rows,
                totalDebit: 1500,
                totalCredit: 400,
                closingBalance: 1300,
            });
        }),
    );
    return calls;
}

const renderTab = () =>
    renderWithTheme(
        <SessionProvider>
            <StatementTab account={accountDto() as unknown as CurrentAccountDto} />
        </SessionProvider>,
    );

describe("StatementTab", () => {
    it("Statement_ShowsOpeningRunningAndTotals", async () => {
        setup([
            row(),
            row({ id: "e2", type: "creditNote", documentNo: "T-1", description: "Tahsilat", debit: 0, credit: 400, dueDate: null, runningBalance: 1300 }),
        ]);

        renderTab();

        expect(await screen.findByText("F-100")).toBeInTheDocument();
        expect(screen.getByText("Devreden bakiye")).toBeInTheDocument();
        expect(screen.getByText("200,00 ₺ (B)")).toBeInTheDocument();
        expect(screen.getByText("Borç dekontu")).toBeInTheDocument();
        expect(screen.getByText("Alacak dekontu")).toBeInTheDocument();
        expect(screen.getAllByText("1.300,00 ₺ (B)").length).toBeGreaterThanOrEqual(2);
        const totals = screen.getByText("Toplam").closest("tr")!;
        expect(totals).toHaveTextContent("1.500,00 ₺");
        expect(totals).toHaveTextContent("400,00 ₺");
        expect(totals).toHaveTextContent("1.300,00 ₺ (B)");
    });

    it("Statement_CancelledRowStruck", async () => {
        setup([row({ cancelled: true, cancelReason: "Hatalı giriş" })]);

        renderTab();

        const cell = await screen.findByText("F-100");
        expect(cell).toHaveStyle({ textDecoration: "line-through" });
        expect(screen.getByLabelText("İptal nedeni: Hatalı giriş")).toBeInTheDocument();
    });

    it("Statement_ApplyRangeRefetches", async () => {
        const calls = setup([row()]);
        const { user } = renderTab();
        await screen.findByText("F-100");
        expect(calls[0].get("from")).toMatch(/^\d{4}-01-01$/);

        const from = screen.getByLabelText("Başlangıç");
        await user.clear(from);
        await user.type(from, "2026-02-01");
        const to = screen.getByLabelText("Bitiş");
        await user.clear(to);
        await user.type(to, "2026-02-28");
        await user.click(screen.getByRole("button", { name: "Uygula" }));

        await waitFor(() => expect(calls.length).toBe(2));
        expect(calls[1].get("from")).toBe("2026-02-01");
        expect(calls[1].get("to")).toBe("2026-02-28");
    });

    it("Statement_PrintCallsWindowPrint", async () => {
        setup([row()]);
        const print = vi.spyOn(window, "print").mockImplementation(() => {});
        const { user } = renderTab();
        await screen.findByText("F-100");

        await user.click(screen.getByRole("button", { name: "Yazdır" }));

        expect(print).toHaveBeenCalledTimes(1);
        expect(screen.getByText("Demo Firma")).toBeInTheDocument();
    });
});
