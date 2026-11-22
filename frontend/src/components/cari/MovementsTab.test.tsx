import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { accountDto, problem } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import MovementsTab from "./MovementsTab";

const BASE = "*/api/erp/current-accounts/acc-1";

const dto = (overrides: Record<string, unknown> = {}) => accountDto(overrides) as unknown as CurrentAccountDto;

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

function setup(rows: unknown[] = []) {
    const calls = { statement: [] as URLSearchParams[], posted: [] as Record<string, unknown>[], cancelled: [] as Record<string, unknown>[] };
    server.use(
        http.get(`${BASE}/statement`, ({ request }) => {
            calls.statement.push(new URL(request.url).searchParams);
            return HttpResponse.json({ openingBalance: 0, rows, totalDebit: 0, totalCredit: 0, closingBalance: 0, currency: "TRY" });
        }),
        http.get(BASE, () => HttpResponse.json(accountDto())),
        http.post(`${BASE}/entries`, async ({ request }) => {
            const body = (await request.json()) as Record<string, unknown>;
            calls.posted.push(body);
            return HttpResponse.json({ entry: row(), balance: 1500, creditLimitExceeded: false }, { status: 201 });
        }),
        http.post(`${BASE}/entries/:entryId/cancel`, async ({ request }) => {
            calls.cancelled.push((await request.json()) as Record<string, unknown>);
            return new HttpResponse(null, { status: 204 });
        }),
    );
    return calls;
}

const renderTab = (account = dto(), canMove = true) =>
    renderWithTheme(<MovementsTab account={account} canMove={canMove} onChanged={vi.fn()} />);

describe("MovementsTab", () => {
    it("Movements_ListsRowsNewestFirst_AllTimeRange", async () => {
        const calls = setup([row({ id: "e1", documentNo: "F-100" }), row({ id: "e2", documentNo: "F-200", date: "2026-03-09" })]);

        renderTab();

        await screen.findByText("F-200");
        const bodyRows = screen.getAllByRole("row").slice(1);
        expect(within(bodyRows[0]).getByText("F-200")).toBeInTheDocument();
        expect(within(bodyRows[1]).getByText("F-100")).toBeInTheDocument();
        expect(within(bodyRows[0]).getByText("09.03.2026")).toBeInTheDocument();
        expect(calls.statement[0].get("from")).toBe("1900-01-01");
        expect(calls.statement[0].get("to")).toBe("9999-12-31");
    });

    it("Movements_ShowsOnlyLast100", async () => {
        const rows = Array.from({ length: 120 }, (_, i) => row({ id: `e${i}`, documentNo: `D-${i}` }));
        setup(rows);

        renderTab();

        await screen.findByText("D-119");
        expect(screen.getAllByRole("row")).toHaveLength(101);
        expect(screen.queryByText("D-19")).not.toBeInTheDocument();
        expect(screen.getByText("D-20")).toBeInTheDocument();
    });

    it("Movements_DebitDueDatePrefilledFromCard", async () => {
        setup();
        renderTab(dto({ defaultDueDays: 30 }));

        fireEvent.change(await screen.findByLabelText("Tarih"), { target: { value: "2026-03-01" } });

        expect(screen.getByLabelText("Vade tarihi")).toHaveValue("2026-03-31");
    });

    it("Movements_EditedDueDateIsKept", async () => {
        setup();
        renderTab(dto({ defaultDueDays: 30 }));

        fireEvent.change(await screen.findByLabelText("Tarih"), { target: { value: "2026-03-01" } });
        fireEvent.change(screen.getByLabelText("Vade tarihi"), { target: { value: "2026-05-01" } });
        fireEvent.change(screen.getByLabelText("Tarih"), { target: { value: "2026-03-10" } });

        expect(screen.getByLabelText("Vade tarihi")).toHaveValue("2026-05-01");
    });

    it("Movements_SubmitDebit_SendsBodyAndRefreshesList", async () => {
        const calls = setup();
        const onChanged = vi.fn();
        const { user } = renderWithTheme(<MovementsTab account={dto()} canMove onChanged={onChanged} />);

        fireEvent.change(await screen.findByLabelText("Tarih"), { target: { value: "2026-03-01" } });
        await user.type(screen.getByLabelText("Tutar"), "1.500,50");
        await user.type(screen.getByLabelText("Belge no"), "F-1");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        await waitFor(() => expect(onChanged).toHaveBeenCalled());
        expect(calls.posted[0]).toMatchObject({ type: "debit", date: "2026-03-01", amount: 1500.5, documentNo: "F-1", dueDate: "2026-03-31" });
        expect(calls.statement.length).toBe(2);
    });

    it("Movements_AmountValidation", async () => {
        const calls = setup();
        const { user } = renderTab();

        await user.type(await screen.findByLabelText("Tutar"), "abc");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        expect(screen.getByText(/Örn\. 1\.500,00/)).toBeInTheDocument();
        expect(calls.posted).toHaveLength(0);
    });

    it("Movements_OpeningRequiresDirection", async () => {
        const calls = setup();
        const { user } = renderTab();

        await user.click(await screen.findByRole("combobox", { name: "Tür" }));
        await user.click(screen.getByRole("option", { name: "Açılış" }));
        await user.type(screen.getByLabelText("Tutar"), "100");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        expect(screen.getByText("Yön seçin (Borç veya Alacak).")).toBeInTheDocument();
        expect(calls.posted).toHaveLength(0);
    });

    it("Movements_CreditLimitWarningShown", async () => {
        setup();
        server.use(
            http.post(`${BASE}/entries`, () => HttpResponse.json({ entry: row(), balance: 60000, creditLimitExceeded: true }, { status: 201 })),
        );
        const { user } = renderTab();

        await user.type(await screen.findByLabelText("Tutar"), "60000");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        expect(await screen.findByText(/kredi limitini aştı/)).toBeInTheDocument();
    });

    it("Movements_AccountInactiveErrorShown", async () => {
        setup();
        server.use(http.post(`${BASE}/entries`, () => HttpResponse.json(problem(409, "x", "account_inactive"), { status: 409 })));
        const { user } = renderTab();

        await user.type(await screen.findByLabelText("Tutar"), "10");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        expect(await screen.findByText("Pasif cari hesaba hareket girilemez.")).toBeInTheDocument();
    });

    it("Movements_OpeningExistsErrorShown", async () => {
        setup();
        server.use(http.post(`${BASE}/entries`, () => HttpResponse.json(problem(409, "x", "opening_exists"), { status: 409 })));
        const { user } = renderTab();

        await user.click(await screen.findByRole("combobox", { name: "Tür" }));
        await user.click(screen.getByRole("option", { name: "Açılış" }));
        await user.click(screen.getByRole("combobox", { name: "Yön" }));
        await user.click(screen.getByRole("option", { name: "Borç" }));
        await user.type(screen.getByLabelText("Tutar"), "10");
        await user.click(screen.getByRole("button", { name: "Hareketi kaydet" }));

        expect(await screen.findByText("Bu carinin zaten bir açılış bakiyesi var.")).toBeInTheDocument();
    });

    it("Movements_FormHiddenWithoutPermission", async () => {
        setup([row()]);

        renderTab(dto(), false);

        await screen.findByText("F-100");
        expect(screen.queryByLabelText("Tutar")).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "İptal et" })).not.toBeInTheDocument();
    });

    it("Movements_FormHiddenWhenInactive", async () => {
        setup();

        renderTab(dto({ isActive: false }));

        expect(await screen.findByText(/Pasif cari hesaba hareket girilemez/)).toBeInTheDocument();
        expect(screen.queryByLabelText("Tutar")).not.toBeInTheDocument();
    });

    it("Movements_CancelRequiresReason", async () => {
        const calls = setup([row()]);
        const { user } = renderTab();

        await user.click(await screen.findByRole("button", { name: "İptal et" }));
        const dialog = screen.getByRole("dialog");
        await user.click(within(dialog).getByRole("button", { name: "Hareketi iptal et" }));

        expect(within(dialog).getByText("İptal nedeni 2 ile 200 karakter arasında olmalı.")).toBeInTheDocument();
        expect(calls.cancelled).toHaveLength(0);

        await user.type(within(dialog).getByLabelText("İptal nedeni"), "Hatalı giriş");
        await user.click(within(dialog).getByRole("button", { name: "Hareketi iptal et" }));

        await waitFor(() => expect(calls.cancelled).toEqual([{ reason: "Hatalı giriş" }]));
        await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    });

    it("Movements_CancelledRowStruckThroughWithReason", async () => {
        setup([row({ cancelled: true, cancelReason: "Hatalı giriş" })]);

        renderTab();

        const cell = await screen.findByText("F-100");
        expect(cell).toHaveStyle({ textDecoration: "line-through" });
        expect(screen.getByText(/Hatalı giriş/)).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "İptal et" })).not.toBeInTheDocument();
    });

    it("Movements_CancelNotManualErrorShown", async () => {
        setup([row()]);
        server.use(http.post(`${BASE}/entries/:id/cancel`, () => HttpResponse.json(problem(409, "x", "not_manual"), { status: 409 })));
        const { user } = renderTab();

        await user.click(await screen.findByRole("button", { name: "İptal et" }));
        const dialog = screen.getByRole("dialog");
        await user.type(within(dialog).getByLabelText("İptal nedeni"), "Hatalı giriş");
        await user.click(within(dialog).getByRole("button", { name: "Hareketi iptal et" }));

        expect(await within(dialog).findByText(/kaynak belgesinden iptal edin/)).toBeInTheDocument();
    });
});
