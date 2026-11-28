import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import QuickButtonDesigner from "./QuickButtonDesigner";

vi.setConfig({ testTimeout: 20000 });

describe("QuickButtonDesigner", () => {
    it("ButtonDesigner_AssignItemToCell", async () => {
        server.use(
            http.get("*/api/erp/catalog/items", () =>
                HttpResponse.json([{ productId: "p1", variantId: null, kind: "product", code: "U00001", name: "Kalem", baseUnit: "adet", vatRate: 20, soldByMeasure: false, measureUnit: null, tracksSerialLot: false, salePrice: null, purchasePrice: null, units: [], isActive: true }]),
            ),
        );
        const onChange = vi.fn();
        const { user } = renderWithTheme(<QuickButtonDesigner pages={[[]]} onChange={onChange} />);

        await user.click(screen.getByRole("button", { name: "Hücre 3" }));
        const dialog = await screen.findByRole("dialog");
        await user.type(within(dialog).getByLabelText("Kalem ara"), "Kalem");
        await user.click(await screen.findByRole("option", { name: "U00001 · Kalem" }, { timeout: 4000 }));
        await user.type(within(dialog).getByLabelText("Hazır miktar"), "2");
        await user.click(within(dialog).getByRole("button", { name: "Uygula" }));

        await waitFor(() => expect(onChange).toHaveBeenCalled());
        const pages = onChange.mock.calls[0][0];
        expect(pages[0]).toHaveLength(36);
        expect(pages[0][2]).toMatchObject({ productId: "p1", variantId: null, code: "U00001", name: "Kalem", presetQty: 2 });
        expect(pages[0][0]).toBeNull();
    });
});
