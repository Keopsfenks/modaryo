import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import DispatchDialog from "./DispatchDialog";
import { CARRIERS, shipmentDto } from "./shippingFixtures";

type Posted = { carrierCode: string; trackingNo: string | null; vehicle: string | null };

function setup() {
    const posted: Posted[] = [];
    server.use(
        http.post("*/api/erp/sales/shipments/sv-1/ship", async ({ request }) => {
            posted.push((await request.json()) as Posted);
            return HttpResponse.json(shipmentDto({ status: "shipped" }));
        }),
    );
    const onDone = vi.fn();
    const view = renderWithTheme(
        <DispatchDialog open carriers={CARRIERS} shipments={[{ id: "sv-1", number: "SV2026000001", customerName: "Akın Tekstil" }]} onClose={() => undefined} onDone={onDone} />,
    );
    return { ...view, posted, onDone };
}

describe("DispatchDialog", () => {
    it("DispatchDialog_TrackingRequiredForCarrier", async () => {
        const { user, posted, onDone } = setup();

        await user.selectOptions(screen.getByRole("combobox", { name: "Kargo firması" }), "yurtici");
        await user.click(screen.getByRole("button", { name: "Sevk et" }));
        // Kendi aracımız dışında takip numarası zorunludur; istek gitmez.
        expect(await screen.findByText("Takip numarası zorunludur.")).toBeInTheDocument();
        expect(posted).toHaveLength(0);

        await user.type(screen.getByRole("textbox", { name: "SV2026000001 takip numarası" }), "TR123");
        await user.click(screen.getByRole("button", { name: "Sevk et" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ carrierCode: "yurtici", trackingNo: "TR123" });
        await waitFor(() => expect(onDone).toHaveBeenCalledTimes(1));
    });

    it("DispatchDialog_OwnVehicleNeedsNoTracking", async () => {
        const { user, posted } = setup();

        await user.selectOptions(screen.getByRole("combobox", { name: "Kargo firması" }), "kendi-arac");
        expect(screen.queryByRole("textbox", { name: /takip numarası/ })).not.toBeInTheDocument();
        await user.type(screen.getByRole("textbox", { name: "Araç" }), "34 ABC 123");
        await user.click(screen.getByRole("button", { name: "Sevk et" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ carrierCode: "kendi-arac", trackingNo: null, vehicle: "34 ABC 123" });
    });
});
