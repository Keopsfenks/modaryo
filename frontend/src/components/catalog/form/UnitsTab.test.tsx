import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import UnitsTab from "./UnitsTab";

describe("UnitsTab", () => {
    it("UnitsTab_FactorValidation", () => {
        const rows = [
            { unit: "koli", factor: "0", barcode: "" },
            { unit: "paket", factor: "1,1234567", barcode: "" },
            { unit: "kg", factor: "12,5", barcode: "" },
        ];
        renderWithTheme(<UnitsTab units={rows} baseUnit="adet" unitOptions={["adet", "koli", "paket", "kg"]} onChange={vi.fn()} />);

        expect(screen.getAllByText("Katsayı sıfırdan büyük olmalı, en fazla 6 ondalık.")).toHaveLength(2);
        expect(screen.getByText(/1 koli =/)).toBeInTheDocument();
    });

    it("UnitsTab_AddsRow", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<UnitsTab units={[]} baseUnit="adet" unitOptions={["adet", "koli"]} onChange={onChange} />);

        await user.click(screen.getByRole("button", { name: "Birim ekle" }));

        expect(onChange).toHaveBeenCalledWith([{ unit: "", factor: "", barcode: "" }]);
    });
});
