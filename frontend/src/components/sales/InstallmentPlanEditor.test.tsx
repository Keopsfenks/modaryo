import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import InstallmentPlanEditor from "./InstallmentPlanEditor";
import { splitPlan } from "./salesModel";

describe("InstallmentPlanEditor", () => {
    it("InstallmentPlan_EqualSplitRemainderLast_HaliValues", async () => {
        const onSave = vi.fn().mockResolvedValue(undefined);
        const { user } = renderWithTheme(<InstallmentPlanEditor total={7360} currency="TRY" plan={null} defaultCount={2} onSave={onSave} />);

        const down = screen.getByRole("textbox", { name: "Peşinat" });
        await user.clear(down);
        await user.type(down, "2453,33");
        await user.click(screen.getByRole("button", { name: "Eşit böl" }));

        expect(screen.getByRole("textbox", { name: "Taksit 1 tutar" })).toHaveValue("2453,33");
        expect(screen.getByRole("textbox", { name: "Taksit 2 tutar" })).toHaveValue("2453,34");

        await user.click(screen.getByRole("button", { name: "Planı kaydet" }));
        expect(onSave).toHaveBeenCalledTimes(1);
        expect(onSave.mock.calls[0][0]).toMatchObject({ downPayment: 2453.33 });
        expect(onSave.mock.calls[0][0].rows.map((r: { amount: number }) => r.amount)).toEqual([2453.33, 2453.34]);
    });

    it("InstallmentPlan_MismatchBlocksSave", async () => {
        const onSave = vi.fn();
        const { user } = renderWithTheme(<InstallmentPlanEditor total={1000} currency="TRY" plan={null} defaultCount={2} onSave={onSave} />);

        await user.click(screen.getByRole("button", { name: "Eşit böl" }));
        const first = screen.getByRole("textbox", { name: "Taksit 1 tutar" });
        await user.clear(first);
        await user.type(first, "100");

        expect(screen.getByRole("button", { name: "Planı kaydet" })).toBeDisabled();
        expect(screen.getAllByText(/eşit olmalıdır/).length).toBeGreaterThan(0);
    });

    it("SplitPlan_RemainderGoesToLastRow", () => {
        expect(splitPlan(100, 0, 3, "2026-12-01").map((r) => r.amount)).toEqual(["33,33", "33,33", "33,34"]);
        expect(splitPlan(7360, 2453.33, 2, "2026-12-01").map((r) => r.dueDate)).toEqual(["2026-12-01", "2027-01-01"]);
    });

    it("InstallmentPlan_ConsumedPlanIsReadOnly", () => {
        renderWithTheme(
            <InstallmentPlanEditor
                total={1000}
                currency="TRY"
                plan={{ downPayment: 0, downPaymentRemaining: 0, installmentCount: 2, firstDueDate: "2026-12-01", rows: [{ sequence: 1, dueDate: "2026-12-01", amount: 500, remaining: 0 }, { sequence: 2, dueDate: "2027-01-01", amount: 500, remaining: 500 }] }}
                onSave={vi.fn()}
            />,
        );
        expect(screen.getByText(/faturalarca kullanılmaya başlandığı/)).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Planı kaydet" })).not.toBeInTheDocument();
    });
});
