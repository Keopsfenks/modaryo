import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import AccountDetail from "@/app/(panel)/cari/[id]/AccountDetail";
import { accountDto, addressDto, problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(account: Record<string, unknown> = {}, permissions = ["cari.read", "cari.manage", "cari.movements"]) {
    const state = { account: accountDto(account) };
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/current-accounts/acc-1", () => HttpResponse.json(state.account)),
        http.get("*/api/erp/current-accounts/acc-1/statement", () =>
            HttpResponse.json({ openingBalance: 0, rows: [], totalDebit: 0, totalCredit: 0, closingBalance: 0, currency: "TRY" }),
        ),
    );
    return state;
}

const render = () =>
    renderWithTheme(
        <SessionProvider>
            <AccountDetail id="acc-1" />
        </SessionProvider>,
    );

describe("AccountDetail", () => {
    beforeEach(() => resetNavigation("/cari/acc-1"));

    it("Detail_ShowsHeader", async () => {
        resetNavigation("/cari/acc-1", "sekme=adresler");
        setup();

        render();

        expect(await screen.findByRole("heading", { name: "Akın Tekstil" })).toBeInTheDocument();
        expect(screen.getByText("C-0001")).toBeInTheDocument();
        expect(screen.getByText("Müşteri")).toBeInTheDocument();
        expect(screen.getByText("1.250,50 ₺ (B)")).toBeInTheDocument();
        expect(screen.getByText("50.000,00 ₺")).toBeInTheDocument();
        expect(screen.getByText("30 gün")).toBeInTheDocument();
    });

    it("Detail_TabFromQueryParam", async () => {
        resetNavigation("/cari/acc-1", "sekme=adresler");
        setup({ addresses: [addressDto()] });

        render();

        expect(await screen.findByText("Atatürk Cad. 1")).toBeInTheDocument();
        expect(screen.getByRole("tab", { name: "Adresler" })).toHaveAttribute("aria-selected", "true");
    });

    it("Detail_TabClickWritesQueryParam", async () => {
        setup();
        const { user } = render();

        await user.click(await screen.findByRole("tab", { name: "Hareketler" }));

        expect(router.replace).toHaveBeenCalledWith("/cari/acc-1?sekme=hareketler", { scroll: false });
    });

    it("Detail_DeleteHiddenWhenHasMovements", async () => {
        setup({ hasMovements: true });

        render();

        await screen.findByRole("heading", { name: "Akın Tekstil" });
        expect(screen.queryByRole("button", { name: "Sil" })).not.toBeInTheDocument();
    });

    it("Detail_ReadOnlyWithoutManage", async () => {
        setup({}, ["cari.read"]);

        render();

        await screen.findByRole("heading", { name: "Akın Tekstil" });
        expect(screen.queryByRole("button", { name: "Sil" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Pasifleştir" })).not.toBeInTheDocument();
        expect(screen.getByLabelText("Ad / Unvan")).toBeDisabled();
    });

    it("Detail_Delete_ConfirmsAndNavigates", async () => {
        setup();
        server.use(http.delete("*/api/erp/current-accounts/acc-1", () => new HttpResponse(null, { status: 204 })));
        const { user } = render();

        await user.click(await screen.findByRole("button", { name: "Sil" }));
        await user.click(screen.getByRole("button", { name: "Evet, sil" }));

        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/finans/cari-hesaplar"));
    });

    it("Detail_Delete_HasMovementsSuggestsDeactivate", async () => {
        setup();
        server.use(
            http.delete("*/api/erp/current-accounts/acc-1", () =>
                HttpResponse.json(problem(409, "Hareketi olan cari silinemez.", "has_movements"), { status: 409 }),
            ),
        );
        const { user } = render();

        await user.click(await screen.findByRole("button", { name: "Sil" }));
        await user.click(screen.getByRole("button", { name: "Evet, sil" }));

        expect(await screen.findByText(/pasifleştirin/)).toBeInTheDocument();
        expect(router.push).not.toHaveBeenCalled();
    });

    it("Detail_Deactivate_UpdatesBadge", async () => {
        const state = setup();
        server.use(
            http.post("*/api/erp/current-accounts/acc-1/deactivate", () => {
                state.account = accountDto({ isActive: false });
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = render();
        await screen.findByRole("heading", { name: "Akın Tekstil" });
        expect(screen.queryByText("Pasif")).not.toBeInTheDocument();

        await user.click(screen.getByRole("button", { name: "Pasifleştir" }));

        expect(await screen.findByText("Pasif")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Etkinleştir" })).toBeInTheDocument();
    });

    it("OpenInvoicesTab_ListsIssuedInvoicesWithOpenBalance", async () => {
        resetNavigation("/cari/acc-1", "sekme=acik-faturalar");
        setup();
        server.use(
            http.get("*/api/erp/current-accounts/acc-1/statement", () =>
                HttpResponse.json({
                    openingBalance: 0,
                    rows: [
                        { id: "e-1", date: "2026-03-01", type: "debitNote", documentNo: "F000001", description: null, debit: 120, credit: 0, dueDate: "2026-03-31", runningBalance: 120, cancelled: false, cancelReason: null, source: "invoice", sourceId: "inv-1" },
                        { id: "e-2", date: "2026-03-02", type: "debitNote", documentNo: null, description: "Elle", debit: 50, credit: 0, dueDate: null, runningBalance: 170, cancelled: false, cancelReason: null, source: "manual", sourceId: null },
                    ],
                    totalDebit: 170,
                    totalCredit: 0,
                    closingBalance: 170,
                    currency: "TRY",
                }),
            ),
            http.get("*/api/erp/current-accounts/acc-1/due-items", () =>
                HttpResponse.json([
                    { entryId: "e-1", date: "2026-03-01", dueDate: "2026-03-31", direction: "receivable", remaining: 120, daysOverdue: 0 },
                    { entryId: "e-2", date: "2026-03-02", dueDate: "2026-04-02", direction: "receivable", remaining: 50, daysOverdue: 0 },
                ]),
            ),
        );

        render();

        const link = await screen.findByRole("link", { name: "F000001" });
        expect(link).toHaveAttribute("href", "/satis/faturalar/inv-1");
        expect(screen.getAllByRole("row")).toHaveLength(2);
    });

    it("OpenInvoicesTab_RequestsTheStatementWithoutTheDefaultYearWindow", async () => {
        resetNavigation("/cari/acc-1", "sekme=acik-faturalar");
        setup();
        const seen: URL[] = [];
        server.use(
            http.get("*/api/erp/current-accounts/acc-1/statement", ({ request }) => {
                seen.push(new URL(request.url));
                return HttpResponse.json({ openingBalance: 0, rows: [], totalDebit: 0, totalCredit: 0, closingBalance: 0, currency: "TRY" });
            }),
            http.get("*/api/erp/current-accounts/acc-1/due-items", () => HttpResponse.json([])),
        );

        render();

        await screen.findByText("Açık bakiyesi olan fatura yok.");
        // Varsayılan pencere (1 Ocak - bugün) açık eski/ileri tarihli faturaları düşürür: geniş aralık istenir.
        expect(seen[0].searchParams.get("from")).toBe("2000-01-01");
        expect(seen[0].searchParams.get("to")).toBe("2100-12-31");
    });

    it("Detail_NotFound", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo())),
            http.get("*/api/erp/current-accounts/acc-1", () => HttpResponse.json(problem(404, "Cari bulunamadı."), { status: 404 })),
        );

        render();

        expect(await screen.findByText("Cari bulunamadı.")).toBeInTheDocument();
    });
});
