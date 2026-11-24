import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { InvoiceInput } from "@/lib/api/invoicing";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import TotalsPanel from "./TotalsPanel";

const input: InvoiceInput = {
    kind: "sales",
    branchId: "b-1",
    currentAccountId: "acc-1",
    date: "2026-03-10",
    currency: "TRY",
    affectsStock: false,
    lines: [{ productId: "p-1", unit: "adet", quantity: 3, unitPrice: 33.33, vatIncluded: false, vatRate: 20 }],
};

const totals = { gross: 100, discount: 0, net: 100, otv: 0, vat: 24.5, withheld: 0, payable: 777.77 };

describe("TotalsPanel", () => {
    it("TotalsPanel_UsesServerPreview", async () => {
        const bodies: unknown[] = [];
        server.use(
            http.post("*/api/erp/invoices/preview", async ({ request }) => {
                bodies.push(await request.json());
                // Sunucunun tutarı, istemcinin hesaplayacağından bilerek farklıdır.
                return HttpResponse.json({
                    rate: 1,
                    rateDate: null,
                    rateOverridden: false,
                    lines: [],
                    totals,
                    totalsTl: totals,
                    vatSummary: [{ rate: 20, base: 100, vat: 24.5 }],
                    cariCurrency: "TRY",
                    ledgerAmount: 777.77,
                });
            }),
        );

        renderWithTheme(<TotalsPanel input={input} currency="TRY" />);

        expect(await screen.findByText("777,77 ₺")).toBeInTheDocument();
        expect(screen.getByText("KDV %20 (matrah 100,00 ₺)")).toBeInTheDocument();
        expect(screen.getAllByText("24,50 ₺").length).toBeGreaterThan(0);
        expect(bodies).toHaveLength(1);
    });

    it("TotalsPanel_DebouncesRapidChanges", async () => {
        let calls = 0;
        server.use(
            http.post("*/api/erp/invoices/preview", () => {
                calls++;
                return HttpResponse.json({ rate: 1, rateDate: null, rateOverridden: false, lines: [], totals, totalsTl: totals, vatSummary: [], cariCurrency: "TRY", ledgerAmount: 1 });
            }),
        );

        const { rerender } = renderWithTheme(<TotalsPanel input={input} currency="TRY" />);
        for (const q of [4, 5, 6]) {
            rerender(<TotalsPanel input={{ ...input, lines: [{ ...input.lines[0], quantity: q }] }} currency="TRY" />);
        }

        await screen.findByText("777,77 ₺");
        expect(calls).toBe(1);
    });
});
