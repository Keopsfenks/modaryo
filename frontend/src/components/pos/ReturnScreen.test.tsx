import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { ReturnInput } from "@/lib/api/pos";
import { problem } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { REGISTER, saleDto } from "./posFixtures";
import ReturnScreen from "./ReturnScreen";

vi.setConfig({ testTimeout: 20000 });

describe("ReturnScreen", () => {
    it("ReturnScreen_ScanReceiptLoadsLines", async () => {
        let body: ReturnInput | undefined;
        server.use(
            http.get("*/api/erp/pos/sales/by-number/PSK1202611280001", () => HttpResponse.json(saleDto())),
            http.post("*/api/erp/pos/returns", async ({ request }) => {
                body = (await request.json()) as ReturnInput;
                return HttpResponse.json({ id: "ret-1", number: "IAD1", returnValue: 25, cashRefund: 25, cardRefund: 0, onAccountCredit: 0, exchangeCredit: 0, difference: -25, returnInvoiceId: null, replayed: false, warnings: [], exchangeSale: null });
            }),
        );
        const onDone = vi.fn();
        const { user } = renderWithTheme(<ReturnScreen register={REGISTER} onDone={onDone} onCancel={vi.fn()} />);

        await user.type(screen.getByRole("textbox", { name: "Fiş numarası" }), "PSK1202611280001{Enter}");

        expect(await screen.findByRole("cell", { name: "Kalem" })).toBeInTheDocument();
        await user.type(screen.getByRole("textbox", { name: "İade miktarı Kalem" }), "1");
        expect(screen.getByLabelText("İade değeri")).toHaveTextContent("25,00 ₺");
        await user.click(screen.getByRole("button", { name: "İadeyi tamamla" }));

        await waitFor(() => expect(body).toBeDefined());
        expect(body).toMatchObject({
            saleNumber: "PSK1202611280001",
            registerId: "reg-1",
            lines: [{ saleLineId: "line-1", quantity: 1 }],
            refund: [{ method: "cash", amount: 25 }],
        });
        expect(typeof body?.clientReturnId).toBe("string");
        await waitFor(() => expect(onDone).toHaveBeenCalled());
    });

    it("ReturnScreen_UnknownReceiptShowsError", async () => {
        server.use(http.get("*/api/erp/pos/sales/by-number/YOK", () => HttpResponse.json(problem(404, "Satış bulunamadı.", "not_found"), { status: 404 })));
        const { user } = renderWithTheme(<ReturnScreen register={REGISTER} onDone={vi.fn()} onCancel={vi.fn()} />);

        await user.type(screen.getByRole("textbox", { name: "Fiş numarası" }), "YOK{Enter}");

        expect(await screen.findByText("Satış bulunamadı.")).toBeInTheDocument();
    });
});
