import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { CustomFieldDto } from "@/lib/api/catalog";
import { renderWithTheme } from "@/test/render";
import CustomFieldsTab from "./CustomFieldsTab";

const def = (key: string, type: CustomFieldDto["type"], extra: Partial<CustomFieldDto> = {}): CustomFieldDto => ({
    id: key,
    key,
    label: `Alan ${key}`,
    type,
    options: [],
    required: false,
    appliesToProduct: true,
    appliesToService: true,
    sort: 0,
    ...extra,
});

describe("CustomFieldsTab", () => {
    it("CustomFieldsTab_RendersByType", () => {
        const definitions = [
            def("metin", "text"),
            def("sayi", "number"),
            def("tarih", "date"),
            def("liste", "select", { options: ["A", "B"] }),
            def("evet", "bool"),
            def("sadece_hizmet", "text", { appliesToProduct: false }),
        ];
        renderWithTheme(<CustomFieldsTab definitions={definitions} kind="product" values={{ evet: true }} errors={{}} onChange={vi.fn()} />);

        expect(screen.getByLabelText("Alan metin")).toHaveAttribute("type", "text");
        expect(screen.getByLabelText("Alan sayi")).toHaveAttribute("type", "number");
        expect(screen.getByLabelText("Alan tarih")).toHaveAttribute("type", "date");
        expect(screen.getByLabelText("Alan liste")).toHaveAttribute("aria-haspopup", "listbox");
        expect(screen.getByLabelText("Alan evet")).toBeChecked();
        expect(screen.queryByLabelText("Alan sadece_hizmet")).not.toBeInTheDocument();
    });
});
