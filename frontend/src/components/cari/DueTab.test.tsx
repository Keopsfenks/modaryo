import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { accountDto } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import DueTab from "./DueTab";

describe("DueTab", () => {
    it("Due_OverdueRowsHighlighted", async () => {
        server.use(
            http.get("*/api/erp/current-accounts/acc-1/due-items", () =>
                HttpResponse.json([
                    { entryId: "a", date: "2026-01-01", dueDate: "2026-01-31", direction: "receivable", remaining: 900, daysOverdue: 12 },
                    { entryId: "b", date: "2026-03-01", dueDate: "2099-03-31", direction: "payable", remaining: 300, daysOverdue: 0 },
                ]),
            ),
        );

        renderWithTheme(<DueTab account={accountDto() as unknown as CurrentAccountDto} />);

        const overdue = (await screen.findByText("Tahsil edilecek")).closest("tr")!;
        const current = screen.getByText("Ödenecek").closest("tr")!;
        expect(overdue).toHaveTextContent("31.01.2026");
        expect(overdue).toHaveTextContent("900,00 ₺");
        expect(overdue).toHaveTextContent("12");
        expect(overdue.getAttribute("data-overdue")).toBe("true");
        expect(current.getAttribute("data-overdue")).toBe("false");
    });
});
