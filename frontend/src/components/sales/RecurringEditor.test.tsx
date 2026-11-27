import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import RecurringEditor from "./RecurringEditor";
import { nextDates } from "./recurringModel";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/satis/abonelikler/yeni"));

describe("RecurringEditor", () => {
    it("RecurringEditor_PreviewNextDates", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satis.recurring"] }))),
            http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [], total: 0 })),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <RecurringEditor />
            </SessionProvider>,
        );

        const start = await screen.findByLabelText("Başlangıç");
        await user.clear(start);
        await user.type(start, "2099-01-31");
        const day = screen.getByRole("textbox", { name: "Ayın günü" });
        await user.clear(day);
        await user.type(day, "15");
        await user.selectOptions(screen.getByRole("combobox", { name: "Dönem" }), "quarterly");

        // 31 Ocak başlangıçtan önceki 15 Ocak atlanır; sonrası üç ayda bir ayın 15. günü.
        const dates = screen.getByLabelText("Sonraki üretim tarihleri");
        await waitFor(() => expect(Array.from(dates.children).map((c) => c.textContent)).toEqual(["15.04.2099", "15.07.2099", "15.10.2099", "15.01.2100", "15.04.2100", "15.07.2100"]));

        await user.type(screen.getByLabelText("Bitiş"), "2099-08-01");
        await waitFor(() => expect(Array.from(dates.children).map((c) => c.textContent)).toEqual(["15.04.2099", "15.07.2099"]));
    });

    it("RecurringModel_NextDatesMatchServerCalendar", () => {
        expect(nextDates("monthly", 28, "2026-01-30", null, 3)).toEqual(["2026-02-28", "2026-03-28", "2026-04-28"]);
        expect(nextDates("yearly", 1, "2026-03-01", "2028-03-01", 10)).toEqual(["2026-03-01", "2027-03-01", "2028-03-01"]);
        expect(nextDates("monthly", 1, "2026-01-01", null, 2, "2026-05-02")).toEqual(["2026-06-01", "2026-07-01"]);
        expect(nextDates("monthly", 29, "2026-01-01", null, 2)).toEqual([]);
    });
});
