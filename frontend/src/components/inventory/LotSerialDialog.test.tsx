import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import LotSerialDialog from "./LotSerialDialog";

const base = {
    open: true,
    onClose: () => undefined,
    productId: "p-1",
    variantId: null,
    warehouseId: "w-1",
};

describe("LotSerialDialog", () => {
    it("LotSerialDialog_FefoSuggestionPrefills", async () => {
        const queries: URLSearchParams[] = [];
        server.use(
            http.get("*/api/erp/inventory/tracking/lots/suggest", ({ request }) => {
                queries.push(new URL(request.url).searchParams);
                return HttpResponse.json([
                    { lotId: "l-1", lotNo: "LOT-A", expiry: "2026-12-01", quantity: 3 },
                    { lotId: "l-2", lotNo: "LOT-B", expiry: "2027-03-01", quantity: 2 },
                ]);
            }),
        );
        const onSave = vi.fn();
        const { user } = renderWithTheme(<LotSerialDialog {...base} mode="out" trackingMode="lot" quantity={5} onSave={onSave} />);

        expect(await screen.findByDisplayValue("LOT-A")).toBeInTheDocument();
        expect(screen.getByDisplayValue("LOT-B")).toBeInTheDocument();
        expect(screen.getByDisplayValue("3")).toBeInTheDocument();
        expect(screen.getByDisplayValue("2")).toBeInTheDocument();
        expect(queries[0].get("qty")).toBe("5");
        expect(queries[0].get("warehouseId")).toBe("w-1");

        await user.click(screen.getByRole("button", { name: "Tamam" }));
        expect(onSave).toHaveBeenCalledWith({
            lots: [
                { lotNo: "LOT-A", expiry: "2026-12-01", quantity: 3, lotId: "l-1" },
                { lotNo: "LOT-B", expiry: "2027-03-01", quantity: 2, lotId: "l-2" },
            ],
        });
    });

    it("LotSerialDialog_SerialCountMustMatchQty", async () => {
        const onSave = vi.fn();
        const { user } = renderWithTheme(<LotSerialDialog {...base} mode="in" trackingMode="serial" quantity={3} onSave={onSave} />);

        const ok = screen.getByRole("button", { name: "Tamam" });
        await user.type(screen.getByRole("textbox", { name: "Seri numaraları" }), "SN1{Enter}SN2");
        expect(await screen.findByText(/Seri numarası sayısı miktarla aynı olmalıdır \(2 \/ 3\)/)).toBeInTheDocument();
        expect(ok).toBeDisabled();

        await user.type(screen.getByRole("textbox", { name: "Seri numaraları" }), "{Enter}SN3");
        expect(ok).toBeEnabled();
        await user.click(ok);
        expect(onSave).toHaveBeenCalledWith({ serials: ["SN1", "SN2", "SN3"] });
    });
});
