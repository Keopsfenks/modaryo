import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import Targets from "./Targets";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));
vi.mock("@/components/charts/Chart", () => ({
    default: () => <div data-testid="chart" />,
    baseChartOptions: () => ({}),
    useChartMode: () => "light",
}));

const IDENTITY = "http://localhost:3000/api/identity";
const YEAR = new Date().getFullYear();

type Put = { rows: { userId: string; month: number; amountTry: number }[] };

describe("Targets", () => {
    it("Targets_GridSaves", async () => {
        const puts: { year: string | null; body: Put }[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satis.targets"] }))),
            http.get(`${IDENTITY}/companies/current/members`, () =>
                HttpResponse.json([
                    { userId: "u-1", email: "ayse@x.com", fullName: "Ayşe Temsilci", roleId: "r", roleName: "Üye", roleSystemKey: null, joinedAt: "2026-01-01T00:00:00Z" },
                    { userId: "u-2", email: "can@x.com", fullName: "Can Satış", roleId: "r", roleName: "Üye", roleSystemKey: null, joinedAt: "2026-01-01T00:00:00Z" },
                ]),
            ),
            http.get("*/api/erp/sales/targets", () => HttpResponse.json({ year: YEAR, rows: [{ userId: "u-1", userName: "Ayşe Temsilci", month: 3, amountTry: 10000 }] })),
            http.get("*/api/erp/sales/targets/actuals", () =>
                HttpResponse.json({ year: YEAR, rows: [{ userId: "u-1", userName: "Ayşe Temsilci", month: 3, targetTry: 10000, actualTry: 4000, percent: 40 }] }),
            ),
            http.put("*/api/erp/sales/targets", async ({ request }) => {
                const body = (await request.json()) as Put;
                puts.push({ year: new URL(request.url).searchParams.get("year"), body });
                return HttpResponse.json({ year: YEAR, rows: body.rows.map((r) => ({ ...r, userName: null })) });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <Targets />
            </SessionProvider>,
        );

        const march = await screen.findByRole("textbox", { name: "Ayşe Mart hedef" });
        await waitFor(() => expect(march).toHaveValue("10000"));
        const can = screen.getByRole("textbox", { name: "Can Nisan hedef" });
        await user.type(can, "2500,50");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(puts).toHaveLength(1));
        expect(puts[0].year).toBe(String(YEAR));
        expect(puts[0].body.rows).toEqual([
            { userId: "u-1", month: 3, amountTry: 10000 },
            { userId: "u-2", month: 4, amountTry: 2500.5 },
        ]);
        expect(await screen.findByText("Hedefler kaydedildi.")).toBeInTheDocument();
        expect(screen.getByTestId("chart")).toBeInTheDocument();
    });
});
