import { useState } from "react";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import DueScheduleEditor from "./DueScheduleEditor";
import { dueScheduleError, splitDue, type DueRow } from "./invoiceModel";

function Harness({ total }: { total: number }) {
    const [rows, setRows] = useState<DueRow[] | null>(null);
    return <DueScheduleEditor total={total} rows={rows} onChange={setRows} singleDueDate="2026-04-09" defaultFirstDate="2026-04-01" />;
}

describe("DueScheduleEditor", () => {
    it("DueSchedule_SplitsEquallyRemainderLast", async () => {
        const { user } = renderWithTheme(<Harness total={100} />);

        await user.click(screen.getByRole("button", { name: "Taksitli" }));

        expect(screen.getByRole("textbox", { name: "Vade 1 tutar" })).toHaveValue("33,33");
        expect(screen.getByRole("textbox", { name: "Vade 2 tutar" })).toHaveValue("33,33");
        expect(screen.getByRole("textbox", { name: "Vade 3 tutar" })).toHaveValue("33,34");
        expect(screen.getByLabelText("Vade 2 tarih")).toHaveValue("2026-05-01");
        expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });

    it("DueSchedule_SumMismatch_BlocksSave", async () => {
        const { user } = renderWithTheme(<Harness total={100} />);
        await user.click(screen.getByRole("button", { name: "Taksitli" }));

        const last = screen.getByRole("textbox", { name: "Vade 3 tutar" });
        await user.clear(last);
        await user.type(last, "30");

        expect(await screen.findByRole("alert")).toHaveTextContent(/toplamı \(96,66\) ödenecek tutara \(100,00\) eşit olmalı/);
        // Kaydetme bu işlevle engellenir.
        expect(dueScheduleError(splitDue(100, 3, "2026-04-01").slice(0, 2), 100)).not.toBeNull();
        expect(dueScheduleError(splitDue(100, 3, "2026-04-01"), 100)).toBeNull();
    });
});
