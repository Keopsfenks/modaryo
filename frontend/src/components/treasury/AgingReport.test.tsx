import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import AgingReport from "./AgingReport";

const buckets = { current: 100, "1-30": 200, "31-60": 300, "61-90": 400, "90+": 500 };
const report = {
    asOf: "2026-11-25",
    direction: "receivable",
    rows: [{ currentAccountId: "c-1", code: "C-0001", name: "Akın Tekstil", currency: "TRY", buckets, total: 1500 }],
    totals: [{ currency: "TRY", buckets, total: 1500 }],
};

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const sessionHandler = http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["finans.read"] })));
const render = () =>
    renderWithTheme(
        <SessionProvider>
            <AgingReport />
        </SessionProvider>,
    );

describe("AgingReport", () => {
    beforeEach(() => resetNavigation("/finans"));

    it("AgingReport_BucketsColumns", async () => {
        const requested: string[] = [];
        server.use(
            sessionHandler,
            http.get("*/api/erp/treasury/reports/aging", ({ request }) => {
                requested.push(request.url);
                return HttpResponse.json(report);
            }),
        );
        const { user } = render();

        const row = (await screen.findByText("Akın Tekstil")).closest("tr")!;
        for (const header of ["Vadesi gelmemiş", "1-30 gün", "31-60 gün", "61-90 gün", "90+ gün", "Toplam"]) {
            expect(screen.getByRole("columnheader", { name: header })).toBeInTheDocument();
        }
        expect(within(row).getAllByRole("cell").map((c) => c.textContent)).toEqual(["C-0001", "Akın Tekstil", "100,00 ₺", "200,00 ₺", "300,00 ₺", "400,00 ₺", "500,00 ₺", "1.500,00 ₺"]);
        expect(requested[0]).toContain("direction=receivable");

        await user.click(screen.getByRole("button", { name: "Borçlar" }));
        await screen.findByText("Akın Tekstil");
        expect(requested.at(-1)).toContain("direction=payable");
    });
});
