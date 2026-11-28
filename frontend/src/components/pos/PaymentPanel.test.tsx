import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import PaymentPanel from "./PaymentPanel";

describe("PaymentPanel", () => {
    it("PaymentPanel_ChangeHuge_MixedPayment", async () => {
        const onSubmit = vi.fn();
        const { user } = renderWithTheme(<PaymentPanel total={347.5} customer={null} busy={false} offline={false} onSubmit={onSubmit} />);

        await user.type(screen.getByRole("textbox", { name: "Kart" }), "200");
        await user.type(screen.getByRole("textbox", { name: "Nakit" }), "200");

        expect(screen.getByRole("status", { name: "Para üstü" })).toHaveTextContent("52,50 ₺");
        await user.click(screen.getByRole("button", { name: "Öde" }));
        expect(onSubmit).toHaveBeenCalledWith([
            { method: "cash", amount: 200 },
            { method: "card", amount: 200 },
        ]);
    });

    it("PaymentPanel_CardCannotExceedTotal", async () => {
        const onSubmit = vi.fn();
        const { user } = renderWithTheme(<PaymentPanel total={100} customer={null} busy={false} offline={false} onSubmit={onSubmit} />);

        await user.type(screen.getByRole("textbox", { name: "Kart" }), "150");

        expect(screen.getByText("Kart tutarı toplamı aşamaz.")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Öde" })).toBeDisabled();
    });

    it("PaymentPanel_VeresiyeNeedsCustomer", async () => {
        const onSubmit = vi.fn();
        const { user, rerender } = renderWithTheme(<PaymentPanel total={347.5} customer={null} busy={false} offline={false} onSubmit={onSubmit} />);

        expect(screen.getByRole("button", { name: "Veresiye" })).toBeDisabled();
        expect(screen.getByText("Veresiye için müşteri seçin (F4).")).toBeInTheDocument();

        rerender(<PaymentPanel total={347.5} customer={{ id: "c1", name: "Akın Tekstil" }} busy={false} offline={false} onSubmit={onSubmit} />);
        await user.click(screen.getByRole("button", { name: "Veresiye" }));

        expect(onSubmit).toHaveBeenCalledWith([{ method: "onAccount", amount: 347.5 }]);
    });

    it("PaymentPanel_PendingTotalBlocksPayment", async () => {
        const onSubmit = vi.fn();
        const { user } = renderWithTheme(<PaymentPanel total={100} customer={{ id: "c1", name: "Akın" }} busy={false} offline={false} pending onSubmit={onSubmit} />);

        await user.type(screen.getByRole("textbox", { name: "Nakit" }), "100");

        expect(screen.getByText("Tutar hesaplanıyor...")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Öde" })).toBeDisabled();
        expect(screen.getByRole("button", { name: "Tam" })).toBeDisabled();
        expect(screen.getByRole("button", { name: "Veresiye" })).toBeDisabled();
    });
});
