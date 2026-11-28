import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { slip } from "./posFixtures";
import Slip from "./Slip";

describe("Slip", () => {
    it("Slip_FiscalModeShowsBilgiFisiText", () => {
        renderWithTheme(<Slip slip={slip({ mode: "fiscalReceipt", notes: [] })} />);

        expect(screen.getByText("Bilgi fişidir. Mali değeri yoktur.")).toBeInTheDocument();
        expect(screen.getByText("PSK1202611280001", { selector: "[data-testid=slip-number]" })).toBeInTheDocument();
        expect(screen.getByText("Para üstü")).toBeInTheDocument();
        expect(screen.getByLabelText("Fiş barkodu")).toBeInTheDocument();
    });

    it("Slip_EArchiveModeShowsInvoiceNumber", () => {
        renderWithTheme(<Slip slip={slip({ mode: "eArchiveInvoice", invoiceNumber: "EAR2026000000012", notes: ["e-Arşiv fatura e-posta/SMS ile iletilecektir."] })} />);

        expect(screen.getByText("EAR2026000000012")).toBeInTheDocument();
        expect(screen.getByText("e-Arşiv fatura e-posta/SMS ile iletilecektir.")).toBeInTheDocument();
        expect(screen.queryByText("Bilgi fişidir. Mali değeri yoktur.")).not.toBeInTheDocument();
    });
});
