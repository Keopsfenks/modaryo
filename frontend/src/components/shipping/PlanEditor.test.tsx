import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PlanDto } from "@/lib/api/shipping";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PlanEditor from "./PlanEditor";
import { shipmentDto } from "./shippingFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/sevkiyat/planlar"));

const session = () =>
    http.get(`${IDENTITY}/auth/session`, () =>
        HttpResponse.json(sessionInfo({ permissions: ["sevkiyat.read", "sevkiyat.plan", "sevkiyat.deliver"], modules: ["teslimat-plani"] })),
    );

function stop(id: string, sort: number, number: string, status: "pending" | "delivered" | "failed" = "pending") {
    return { id, sort, shipmentId: `sv-${id}`, shipmentNumber: number, customerName: `Müşteri ${number}`, customerPhone: null, address: null, status, note: null };
}

function plan(overrides: Partial<PlanDto> = {}): PlanDto {
    return {
        id: "pl-1",
        number: "TP2026000001",
        branchId: "b-1",
        date: "2026-11-28",
        vehicle: "34 ABC 123",
        driverUserId: null,
        driverName: "Ali",
        status: "planned",
        version: 1,
        stops: [stop("a", 1, "SV1"), stop("b", 2, "SV2"), stop("c", 3, "SV3")],
        ...overrides,
    };
}

describe("PlanEditor", () => {
    it("PlanEditor_ReorderStops", async () => {
        const puts: { stopIds: string[] }[] = [];
        let current = plan();
        server.use(
            session(),
            http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
            http.get("*/api/erp/shipping/plans/pl-1", () => HttpResponse.json(current)),
            http.put("*/api/erp/shipping/plans/pl-1/stops", async ({ request }) => {
                const body = (await request.json()) as { stopIds: string[] };
                puts.push(body);
                current = { ...current, stops: body.stopIds.map((id, i) => ({ ...current.stops.find((s) => s.id === id)!, sort: i + 1 })) };
                return HttpResponse.json(current);
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <PlanEditor planId="pl-1" />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("button", { name: "SV1 aşağı taşı" }));

        await waitFor(() => expect(puts).toEqual([{ stopIds: ["b", "a", "c"] }]));
        // Yeni sıra ekrana yansır; ilk durak artık SV2.
        await waitFor(() => expect(within(screen.getAllByRole("listitem")[0]).getByText("SV2")).toBeInTheDocument());
        // İlk durak yukarı, son durak aşağı taşınamaz.
        expect(screen.getByRole("button", { name: "SV2 yukarı taşı" })).toBeDisabled();
        expect(screen.getByRole("button", { name: "SV3 aşağı taşı" })).toBeDisabled();
    });

    it("PlanEditor_OnlyOwnVehicleShippedOffered", async () => {
        const created: { shipmentIds: string[]; date: string }[] = [];
        const details = [
            shipmentDto({ id: "sv-own", number: "SVOWN", status: "shipped", carrierCode: "kendi-arac", customerName: "Kendi Müşteri" }),
            shipmentDto({ id: "sv-cargo", number: "SVCARGO", status: "shipped", carrierCode: "yurtici", customerName: "Kargolu Müşteri" }),
            shipmentDto({ id: "sv-draft", number: "SVDRAFT", status: "draft", customerName: "Taslak Müşteri" }),
        ];
        server.use(
            session(),
            http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
            http.get("*/api/erp/sales/shipments", () =>
                HttpResponse.json({
                    items: details.map((d) => ({ id: d.id, number: d.number, status: d.status, salesOrderId: "so", orderNumber: "SS", currentAccountId: "a", customerName: d.customerName, date: d.date, invoiceId: null })),
                    total: details.length,
                }),
            ),
            http.get("*/api/erp/sales/shipments/:id", ({ params }) => HttpResponse.json(details.find((d) => d.id === params.id))),
            http.post("*/api/erp/shipping/plans", async ({ request }) => {
                created.push((await request.json()) as (typeof created)[number]);
                return HttpResponse.json(plan({ id: "pl-9" }), { status: 201 });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <PlanEditor />
            </SessionProvider>,
        );

        // Yalnızca kendi araçla sevk edilmiş (shipped) sevkler önerilir.
        expect(await screen.findByRole("checkbox", { name: /SVOWN/ })).toBeInTheDocument();
        expect(screen.queryByRole("checkbox", { name: /SVCARGO/ })).not.toBeInTheDocument();
        expect(screen.queryByRole("checkbox", { name: /SVDRAFT/ })).not.toBeInTheDocument();

        await user.click(screen.getByRole("checkbox", { name: /SVOWN/ }));
        await user.click(screen.getByRole("button", { name: "Planı oluştur" }));

        await waitFor(() => expect(created).toHaveLength(1));
        expect(created[0].shipmentIds).toEqual(["sv-own"]);
        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/sevkiyat/planlar/pl-9"));
    });

    it("PlanEditor_StopDeliverAndFail", async () => {
        const calls: string[] = [];
        let current = plan({ stops: [stop("a", 1, "SV1"), stop("b", 2, "SV2")] });
        server.use(
            session(),
            http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
            http.get("*/api/erp/shipping/plans/pl-1", () => HttpResponse.json(current)),
            http.post("*/api/erp/shipping/plans/pl-1/stops/b/fail", async ({ request }) => {
                calls.push(`fail:${((await request.json()) as { reason: string }).reason}`);
                current = { ...current, stops: current.stops.map((s) => (s.id === "b" ? { ...s, status: "failed" as const } : s)) };
                return HttpResponse.json(current);
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <PlanEditor planId="pl-1" />
            </SessionProvider>,
        );

        const item = (await screen.findAllByRole("listitem"))[1];
        await user.click(within(item).getByRole("button", { name: "Teslim edilemedi" }));
        await user.type(await screen.findByRole("textbox", { name: /Gerekçe/ }), "Adreste kimse yok");
        await user.click(screen.getByRole("button", { name: "Başarısız işaretle" }));

        await waitFor(() => expect(calls).toEqual(["fail:Adreste kimse yok"]));
        await waitFor(() => expect(within(screen.getAllByRole("listitem")[1]).getByText("Teslim edilemedi")).toBeInTheDocument());
    });
});
