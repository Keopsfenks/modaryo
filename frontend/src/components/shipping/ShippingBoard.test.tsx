import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ShippingBoard from "./ShippingBoard";
import { shipmentDto, shipmentLine } from "./shippingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/sevkiyat"));

const details = [
    shipmentDto({ id: "sv-1", number: "SV1", status: "draft", customerName: "Akın Tekstil", lines: [shipmentLine(), shipmentLine({ id: "sl-2" })] }),
    shipmentDto({
        id: "sv-2",
        number: "SV2",
        status: "shipped",
        customerName: "Bora Mobilya",
        carrierCode: "yurtici",
        trackingNo: "TR99",
        trackingUrl: "https://kargo.example/yurtici?code=TR99",
        parcels: [{ id: "p1", index: 1, weightKg: 2, widthCm: 10, lengthCm: 10, heightCm: 10, desi: 1, contents: [] }],
    }),
    shipmentDto({ id: "sv-3", number: "SV3", status: "packed", customerName: "Ceren Ticaret" }),
    shipmentDto({ id: "sv-4", number: "SV4", status: "delivered", customerName: "Deniz Ltd" }),
];

function setup(permissions = ["sevkiyat.read", "sevkiyat.pick", "sevkiyat.ship", "sevkiyat.deliver"]) {
    const batches: string[][] = [];
    const requests: string[] = [];
    const detailCalls = { count: 0 };
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "M", name: "Merkez", isActive: true, isDefault: true }])),
        http.get("*/api/erp/sales/shipments", ({ request }) => {
            const url = new URL(request.url);
            requests.push(url.search);
            const wanted = (url.searchParams.get("statuses") ?? "").split(",");
            const rows = details.filter((d) => wanted.includes(d.status));
            return HttpResponse.json({
                items: rows.map((d) => ({
                    id: d.id,
                    number: d.number,
                    status: d.status,
                    salesOrderId: "so-1",
                    orderNumber: "SS1",
                    currentAccountId: "a",
                    customerName: d.customerName,
                    date: d.date,
                    invoiceId: null,
                    shippedAt: null,
                    deliveredAt: null,
                    carrierCode: d.carrierCode,
                    carrierName: d.carrierCode ? "Yurtiçi" : null,
                    trackingNo: d.trackingNo,
                    trackingUrl: d.trackingUrl,
                    irsaliyeNo: null,
                    pickListId: null,
                    lineCount: d.lines.length,
                    parcelCount: d.parcels.length,
                    items: d.lines.map((l) => ({ description: l.description, unit: l.unit, quantity: l.quantity, isService: false })),
                    customerEmailMissing: false,
                    created: "2026-11-28T08:00:00Z",
                    branchId: d.branchId,
                })),
                total: rows.length,
            });
        }),
        http.get("*/api/erp/sales/shipments/:id", () => {
            detailCalls.count++;
            return HttpResponse.json({});
        }),
        http.post("*/api/erp/shipping/pick-lists/batch", async ({ request }) => {
            batches.push(((await request.json()) as { shipmentIds: string[] }).shipmentIds);
            return HttpResponse.json({ id: "pk-9" }, { status: 201 });
        }),
    );
    const view = renderWithTheme(
        <SessionProvider>
            <ShippingBoard />
        </SessionProvider>,
    );
    return { ...view, batches, requests, detailCalls };
}

describe("ShippingBoard", () => {
    it("Board_GroupsByStatus", async () => {
        setup();

        const shipped = await screen.findByRole("region", { name: "Yolda" });
        await within(shipped).findByText("Bora Mobilya");
        // Takip bağlantısı ve koli sayısı kartta görünür.
        expect(within(shipped).getByRole("link", { name: "TR99" })).toHaveAttribute("href", "https://kargo.example/yurtici?code=TR99");
        expect(within(shipped).getByText("1 koli")).toBeInTheDocument();
        const draft = screen.getByRole("region", { name: "Taslak" });
        expect(await within(draft).findByText("Akın Tekstil")).toBeInTheDocument();
        expect(within(draft).getByText("2 kalem")).toBeInTheDocument();
        expect(await within(screen.getByRole("region", { name: "Paketlendi" })).findByText("Ceren Ticaret")).toBeInTheDocument();
        expect(await within(screen.getByRole("region", { name: "Teslim edildi" })).findByText("Deniz Ltd")).toBeInTheDocument();
        expect(within(shipped).queryByText("Akın Tekstil")).not.toBeInTheDocument();
    });

    it("Board_UsesListRowsAndStatusFilter_NoDetailCalls", async () => {
        const { requests, detailCalls } = setup();

        await screen.findByText("Bora Mobilya");

        // Canlı durumlar ve bugünün teslimleri iki süzgeçli liste çağrısıyla gelir; sevk başına ayrıntı çağrısı yoktur.
        expect(requests).toHaveLength(2);
        expect(requests.some((q) => q.includes("statuses=draft%2Cpicking%2Cpacked%2Cshipped%2CdeliveryFailed") && q.includes("pageSize=500"))).toBe(true);
        expect(requests.some((q) => q.includes("statuses=delivered") && q.includes("deliveredFrom="))).toBe(true);
        expect(detailCalls.count).toBe(0);
    });

    it("Board_BulkPickList_CreatesBatchAndOpensIt", async () => {
        const { user, batches } = setup();

        await user.click(await screen.findByRole("checkbox", { name: "SV1 seç" }));
        await user.click(screen.getByRole("button", { name: "Toplama listesi" }));

        await waitFor(() => expect(batches).toEqual([["sv-1"]]));
        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/sevkiyat/toplama/pk-9"));
    });
});
