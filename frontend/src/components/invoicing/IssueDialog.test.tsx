import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { SeriesDto } from "@/lib/api/invoicing";
import { renderWithTheme } from "@/test/render";
import IssueDialog from "./IssueDialog";

const series = (over: Partial<SeriesDto>): SeriesDto => ({
    id: "s-1",
    kindGroup: "sales",
    branchId: "b-1",
    mode: "eArchive",
    prefix: "ARS",
    year: 2026,
    nextNumber: 1,
    minWidth: 9,
    isDefault: true,
    isActive: true,
    nextPreview: "ARS2026000000001",
    ...over,
});

const props = { open: true, onClose: vi.fn(), onConfirm: vi.fn(), kindGroup: "sales" as const, branchId: "b-1", year: 2026 };

describe("IssueDialog", () => {
    it("IssueDialog_ShowsModeEArsivForNonTaxpayer", () => {
        renderWithTheme(
            <IssueDialog
                {...props}
                taxpayer={false}
                ebelge={{ enabled: true, defaultScenario: "basic", eIrsaliyeEnabled: false }}
                series={[series({}), series({ id: "s-2", mode: "eInvoice", prefix: "FAT", nextPreview: "FAT2026000000001" })]}
            />,
        );

        expect(screen.getByText("e-Arşiv")).toBeInTheDocument();
        expect(screen.getByText("ARS2026000000001")).toBeInTheDocument();
        expect(screen.queryByText("FAT2026000000001")).not.toBeInTheDocument();
    });

    it("IssueDialog_TaxpayerGetsEInvoice_DisabledGetsPaper", () => {
        const { unmount } = renderWithTheme(
            <IssueDialog
                {...props}
                taxpayer
                ebelge={{ enabled: true, defaultScenario: "basic", eIrsaliyeEnabled: false }}
                series={[series({ mode: "eInvoice", prefix: "FAT", nextPreview: "FAT2026000000001" })]}
            />,
        );
        expect(screen.getByText("e-Fatura")).toBeInTheDocument();
        unmount();

        renderWithTheme(
            <IssueDialog
                {...props}
                taxpayer
                ebelge={{ enabled: false, defaultScenario: "basic", eIrsaliyeEnabled: false }}
                series={[series({ mode: "paper", prefix: "F", nextPreview: "F000001" })]}
            />,
        );
        expect(screen.getByText("Kâğıt")).toBeInTheDocument();
        expect(screen.getByText("F000001")).toBeInTheDocument();
    });
});
