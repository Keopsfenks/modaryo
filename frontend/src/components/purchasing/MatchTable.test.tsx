import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import MatchTable from "./MatchTable";
import { matchRow, poLine } from "./purchasingFixtures";

describe("MatchTable", () => {
    it("MatchTable_VarianceRowsHighlighted", () => {
        const lines = [poLine({ id: "pol-1" }), poLine({ id: "pol-2", productCode: "U00002", productName: "Pantolon" })];
        renderWithTheme(
            <MatchTable
                currency="TRY"
                showPrices
                lines={lines}
                rows={[matchRow({ poLineId: "pol-1" }), matchRow({ poLineId: "pol-2", invoicePrice: 21, priceVariance: true, invoiced: 8, qtyMismatch: true })]}
            />,
        );

        const ok = screen.getByText(/U00001/).closest("tr");
        const flagged = screen.getByText(/U00002/).closest("tr");
        expect(ok).toHaveAttribute("data-flagged", "false");
        expect(flagged).toHaveAttribute("data-flagged", "true");
        expect(screen.getByText("Fiyat farkı")).toBeInTheDocument();
        expect(screen.getByText("Miktar farkı")).toBeInTheDocument();
        expect(screen.getByText("Uyumlu")).toBeInTheDocument();
    });

    it("MatchTable_NoPrices_HidesPriceColumns", () => {
        renderWithTheme(<MatchTable currency="TRY" showPrices={false} lines={[poLine()]} rows={[matchRow({ poPrice: null, receiptPrice: null, invoicePrice: null })]} />);

        expect(screen.queryByText("Sipariş fiyatı")).not.toBeInTheDocument();
        expect(screen.getByText("Mal kabul")).toBeInTheDocument();
    });
});
