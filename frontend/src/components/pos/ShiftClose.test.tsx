import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { SHIFT, shiftReport } from "./posFixtures";
import ShiftClose from "./ShiftClose";

describe("ShiftClose", () => {
    it("ShiftClose_ShowsDifference", async () => {
        let body: unknown;
        server.use(
            http.get("*/api/erp/pos/reports/shift/sh-1", () => HttpResponse.json(shiftReport())),
            http.post("*/api/erp/pos/shifts/sh-1/close", async ({ request }) => {
                body = await request.json();
                return HttpResponse.json(shiftReport({}, { status: "closed", countedCash: 1240.5, expectedCash: 1250.5, difference: -10 }));
            }),
        );
        const onClosed = vi.fn();
        const { user } = renderWithTheme(<ShiftClose shift={SHIFT} onClosed={onClosed} onCancel={vi.fn()} />);

        expect(await screen.findByLabelText("Beklenen nakit")).toHaveTextContent("1.250,50 ₺");
        await user.type(screen.getByRole("textbox", { name: "Sayılan nakit" }), "1240,50");

        expect(screen.getByLabelText("Fark")).toHaveTextContent("-10,00 ₺");

        await user.click(screen.getByRole("button", { name: "Vardiyayı kapat" }));
        await waitFor(() => expect(body).toEqual({ countedCash: 1240.5, note: null }));
        expect(await screen.findByText("Kasa raporu")).toBeInTheDocument();
        expect(screen.getByLabelText("Fark")).toHaveTextContent("-10,00 ₺");
    });
});
