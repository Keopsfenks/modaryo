import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { OrderRef, ShipmentStatus } from "@/lib/api/sales";
import { renderWithTheme } from "@/test/render";
import InvoiceFromShipmentsDialog from "./InvoiceFromShipmentsDialog";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const ref = (id: string, number: string, status: ShipmentStatus): OrderRef<ShipmentStatus> => ({ id, number, status, date: "2026-03-01" });

describe("InvoiceFromShipmentsDialog", () => {
    it("InvoiceFromShipments_CancelledShipmentIsNotListedNorPreselected", () => {
        renderWithTheme(<InvoiceFromShipmentsDialog shipments={[ref("s-1", "SV2026000001", "cancelled"), ref("s-2", "SV2026000002", "shipped")]} onClose={() => undefined} />);

        expect(screen.queryByText(/SV2026000001/)).not.toBeInTheDocument();
        const shipped = screen.getByRole("checkbox", { name: /SV2026000002 · .* · Sevk edildi/ });
        expect(shipped).toBeChecked();
        expect(screen.getAllByRole("checkbox")).toHaveLength(1);
    });
});
