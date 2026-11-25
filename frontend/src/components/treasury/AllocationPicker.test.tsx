import { screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { OpenItemForAllocation } from "@/lib/api/treasury";
import { renderWithTheme } from "@/test/render";
import AllocationPicker from "./AllocationPicker";

const item = (id: string, no: string, due: string, open: number): OpenItemForAllocation => ({
    dueLineId: id,
    entryId: `e-${id}`,
    source: "invoice",
    sourceId: null,
    documentNo: no,
    description: null,
    date: "2026-01-01",
    dueDate: due,
    sequence: 1,
    direction: "receivable",
    amount: open,
    open,
    allocatable: open,
});

const items = [item("l-1", "F1", "2026-02-01", 100), item("l-2", "F2", "2026-03-01", 200)];

describe("AllocationPicker", () => {
    it("AllocationPicker_FifoPrefillEditable", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<AllocationPicker items={items} budget={150} currency="TRY" onChange={onChange} />);

        expect(screen.getByRole("textbox", { name: "F1 taksit 1 tutar" })).toHaveValue("100");
        expect(screen.getByRole("textbox", { name: "F2 taksit 1 tutar" })).toHaveValue("50");
        await waitFor(() =>
            expect(onChange).toHaveBeenLastCalledWith({
                allocations: [
                    { dueLineId: "l-1", amount: 100 },
                    { dueLineId: "l-2", amount: 50 },
                ],
                total: 150,
                valid: true,
            }),
        );

        const second = screen.getByRole("textbox", { name: "F2 taksit 1 tutar" });
        await user.clear(second);
        await user.type(second, "30");

        await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ total: 130, valid: true })));
        await user.click(screen.getByRole("checkbox", { name: "F1 taksit 1 seç" }));
        await waitFor(() => expect(onChange).toHaveBeenLastCalledWith({ allocations: [{ dueLineId: "l-2", amount: 30 }], total: 30, valid: true }));
    });

    it("AllocationPicker_OverOpenBlocked", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<AllocationPicker items={items} budget={null} currency="TRY" onChange={onChange} />);

        await user.click(screen.getByRole("checkbox", { name: "F1 taksit 1 seç" }));
        const field = screen.getByRole("textbox", { name: "F1 taksit 1 tutar" });
        await user.clear(field);
        await user.type(field, "250");

        expect(await screen.findByText("Açık tutarı aşıyor.")).toBeInTheDocument();
        await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ valid: false, allocations: [] })));
    });
});
