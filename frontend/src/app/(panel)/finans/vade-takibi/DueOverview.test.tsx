import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import DueOverview from "./DueOverview";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const summary = {
    byCurrency: [
        {
            currency: "TRY",
            receivable: { overdue: 1000, within30: 2000, later: 3000 },
            payable: { overdue: 10, within30: 20, later: 30 },
        },
        {
            currency: "USD",
            receivable: { overdue: 5, within30: 6, later: 7 },
            payable: { overdue: 0, within30: 0, later: 0 },
        },
    ],
    topOverdue: [{ id: "acc-9", code: "C-0009", name: "Geç Ödeyen Ltd", currency: "TRY", overdueReceivable: 1000, maxDaysOverdue: 45 }],
};

describe("DueOverview", () => {
    beforeEach(() => resetNavigation("/finans/vade-takibi"));

    it("DueOverview_GroupsByCurrency", async () => {
        server.use(http.get("*/api/erp/current-accounts/due-summary", () => HttpResponse.json(summary)));

        renderWithTheme(<DueOverview />);

        const tryGroup = await screen.findByRole("group", { name: "TRY" });
        expect(within(tryGroup).getByText("Tahsil edilecek")).toBeInTheDocument();
        expect(within(tryGroup).getByText("Ödenecek")).toBeInTheDocument();
        expect(within(tryGroup).getByText("3.000,00 ₺")).toBeInTheDocument();
        expect(within(tryGroup).getAllByText("Vadesi geçmiş")).toHaveLength(2);
        const usd = screen.getByRole("group", { name: "USD" });
        expect(within(usd).getByText("$7,00")).toBeInTheDocument();
    });

    it("DueOverview_RowLinksToDueTab", async () => {
        server.use(http.get("*/api/erp/current-accounts/due-summary", () => HttpResponse.json(summary)));
        const { user } = renderWithTheme(<DueOverview />);

        await user.click(await screen.findByText("Geç Ödeyen Ltd"));

        expect(router.push).toHaveBeenCalledWith("/cari/acc-9?sekme=vade");
    });

    it("DueOverview_EmptyState", async () => {
        server.use(http.get("*/api/erp/current-accounts/due-summary", () => HttpResponse.json({ byCurrency: [], topOverdue: [] })));

        renderWithTheme(<DueOverview />);

        expect(await screen.findByText("Açık vadeli kalem yok")).toBeInTheDocument();
    });
});
