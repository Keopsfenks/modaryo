import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ScaleBarcodeSetting } from "@/lib/api/pos";
import { renderWithTheme } from "@/test/render";
import ScaleFormats from "./ScaleFormats";

const formats: ScaleBarcodeSetting[] = [
    { prefix: "27", itemCodeLength: 5, valueLength: 5, valueKind: "weightGrams" },
    { prefix: "28", itemCodeLength: 5, valueLength: 5, valueKind: "priceKurus" },
];

describe("ScaleFormats", () => {
    it("ScaleFormats_LiveParse", async () => {
        const { user } = renderWithTheme(<ScaleFormats value={formats} onChange={vi.fn()} />);
        const input = screen.getByRole("textbox", { name: "Denenecek barkod" });

        await user.type(input, "2712345012348");
        expect(await screen.findByText(/Kalem kodu: 12345/)).toBeInTheDocument();
        expect(screen.getByText(/1,234 kg/)).toBeInTheDocument();

        await user.clear(input);
        await user.type(input, "2712345012345");
        expect(await screen.findByText("Kontrol hanesi hatalı.")).toBeInTheDocument();

        await user.clear(input);
        await user.type(input, "9912345012345");
        expect(await screen.findByText("Hiçbir biçimin önekine uymuyor.")).toBeInTheDocument();
    });

    it("ScaleFormats_AddRowEmitsChange", async () => {
        const onChange = vi.fn();
        const { user } = renderWithTheme(<ScaleFormats value={[]} onChange={onChange} />);

        await user.click(screen.getByRole("button", { name: "Biçim ekle" }));

        expect(onChange).toHaveBeenCalledWith([{ prefix: "", itemCodeLength: 5, valueLength: 5, valueKind: "weightGrams" }]);
    });
});
