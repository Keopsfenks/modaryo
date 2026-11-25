import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CashFlowChart from "./CashFlowChart";

// ApexCharts jsdom'da çizilmez; serilere ulaşmak için grafik bileşeni taklit edilir.
vi.mock("@/components/charts/Chart", () => ({
    default: (props: { series: unknown }) => <pre data-testid="chart">{JSON.stringify(props.series)}</pre>,
    baseChartOptions: () => ({}),
    useChartMode: () => "light",
}));

const sources = (dueLines: number, instruments = 0) => ({ dueLines, instruments, recurring: 0, total: dueLines + instruments });
const report = {
    start: "2026-11-25",
    granularity: "week",
    currency: "TRY",
    opening: 1000,
    closing: 1700,
    periods: [
        { start: "2026-11-25", end: "2026-12-01", inflow: sources(500), outflow: sources(200), net: 300, closing: 1300 },
        { start: "2026-12-02", end: "2026-12-08", inflow: sources(0, 600), outflow: sources(200), net: 400, closing: 1700 },
    ],
};

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const sessionHandler = http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["finans.read"] })));
const render = () =>
    renderWithTheme(
        <SessionProvider>
            <CashFlowChart />
        </SessionProvider>,
    );

describe("CashFlowChart", () => {
    beforeEach(() => resetNavigation("/finans"));

    it("CashFlow_RendersSeries", async () => {
        server.use(
            sessionHandler,
            http.get("*/api/erp/treasury/reports/cash-flow", () => HttpResponse.json(report)));
        render();

        const chart = await screen.findByTestId("chart");
        const series = JSON.parse(chart.textContent ?? "[]") as { name: string; type: string; data: number[] }[];

        expect(series.map((s) => [s.name, s.type, s.data])).toEqual([
            ["Giren", "column", [500, 600]],
            ["Çıkan", "column", [200, 200]],
            ["Bakiye", "line", [1300, 1700]],
        ]);
        expect(screen.getByText(/Açılış bakiyesi/)).toHaveTextContent("1.000,00 ₺");
    });
});
