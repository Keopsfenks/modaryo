import { useState } from "react";
import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { SellableItem } from "@/lib/api/catalog";
import type { InvoiceCodes } from "@/lib/api/invoicing";
import { sellable } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import InvoiceLines from "./InvoiceLines";
import { newInvoiceLine, type InvoiceLineDraft } from "./invoiceModel";

const codes: InvoiceCodes = {
    withholding: [{ code: "T01", name: "Test tevkifat", tenths: 5 }],
    exemption: [{ code: "X01", name: "Test istisna", group: "istisna" }],
};

function Harness({ initial }: { initial: InvoiceLineDraft[] }) {
    const [lines, setLines] = useState(initial);
    return <InvoiceLines lines={lines} onChange={setLines} codes={codes} purchase={false} currentAccountId="acc-1" date="2026-03-10" currency="TRY" affectsStock={false} warehouseId="" />;
}

const item = (over: Record<string, unknown>) => newInvoiceLine(sellable(over) as unknown as SellableItem);

describe("InvoiceLines", () => {
    it("InvoiceLines_MeasureProductShowsDimensions", async () => {
        const fabric = item({ code: "KUM1", name: "Kumaş", soldByMeasure: true, measureUnit: "m2" });
        const plain = item({ code: "U1", name: "Tişört" });
        const { user } = renderWithTheme(<Harness initial={[fabric, plain]} />);

        expect(screen.getByRole("textbox", { name: "KUM1 en" })).toBeInTheDocument();
        expect(screen.getByRole("textbox", { name: "KUM1 boy" })).toBeInTheDocument();
        expect(screen.queryByRole("textbox", { name: "KUM1 miktar" })).not.toBeInTheDocument();
        expect(screen.getByRole("textbox", { name: "U1 miktar" })).toBeInTheDocument();
        expect(screen.queryByRole("textbox", { name: "U1 en" })).not.toBeInTheDocument();

        await user.type(screen.getByRole("textbox", { name: "KUM1 en" }), "2");
        await user.type(screen.getByRole("textbox", { name: "KUM1 boy" }), "3,5");
        expect(screen.getByText("7 m²")).toBeInTheDocument();
    });

    it("InvoiceLines_ExemptionRequiresZeroVat", async () => {
        const { user } = renderWithTheme(<Harness initial={[item({ code: "U1", name: "Tişört", vatRate: 20 })]} />);
        const exemption = screen.getByRole("combobox", { name: "U1 istisna" });
        const vat = screen.getByRole("combobox", { name: "U1 KDV" });

        expect(exemption).toBeDisabled();

        await user.selectOptions(vat, "0");
        expect(exemption).toBeEnabled();
        await user.selectOptions(exemption, "X01");
        expect(screen.getByRole("textbox", { name: "U1 istisna gerekçesi" })).toBeInTheDocument();
        expect(screen.getByRole("combobox", { name: "U1 tevkifat" })).toBeDisabled();

        await user.selectOptions(vat, "10");
        expect(exemption).toBeDisabled();
        expect(exemption).toHaveValue("");
        expect(screen.queryByRole("textbox", { name: "U1 istisna gerekçesi" })).not.toBeInTheDocument();
    });
});
