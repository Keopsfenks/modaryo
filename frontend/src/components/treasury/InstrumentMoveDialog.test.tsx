import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { instrumentDto } from "./fixtures";
import InstrumentMoveDialog from "./InstrumentMoveDialog";

function setup() {
    server.use(http.get("*/api/erp/treasury/accounts", () => HttpResponse.json([])));
}

describe("InstrumentMoveDialog", () => {
    it("InstrumentMoveDialog_OnlyAllowedTransitions", async () => {
        setup();
        const { user } = renderWithTheme(<InstrumentMoveDialog open instrument={instrumentDto()} onClose={vi.fn()} onMoved={vi.fn()} />);

        await user.click(await screen.findByRole("combobox", { name: "İşlem" }));
        const options = (await screen.findAllByRole("option")).map((o) => o.textContent);

        expect(options).toEqual(["Tedarikçiye ciro et", "Bankaya tahsile ver", "Müşteriye iade et"]);
    });

    it("InstrumentMoveDialog_IssuedOffersPaidAndReturn", async () => {
        setup();
        const { user } = renderWithTheme(<InstrumentMoveDialog open instrument={instrumentDto({ direction: "issued", status: "issued" })} onClose={vi.fn()} onMoved={vi.fn()} />);

        await user.click(await screen.findByRole("combobox", { name: "İşlem" }));

        expect((await screen.findAllByRole("option")).map((o) => o.textContent)).toEqual(["Ödendi", "Tedarikçiden iade alındı"]);
    });

    it("InstrumentMoveDialog_NoTransitionsFromFinalStatus", async () => {
        setup();
        renderWithTheme(<InstrumentMoveDialog open instrument={instrumentDto({ status: "collected" })} onClose={vi.fn()} onMoved={vi.fn()} />);

        expect(await screen.findByText(/Bu durumdan başka işlem yapılamaz/)).toBeInTheDocument();
    });
});
