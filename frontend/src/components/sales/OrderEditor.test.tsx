import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import OrderEditor from "./OrderEditor";
import { orderDto, orderLine } from "./salesFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(permissions: string[], modules: string[] = []) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions, modules }))),
        http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
        http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
        http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [], total: 0 })),
        http.get("*/api/erp/catalog/products/*", () => HttpResponse.json({}, { status: 404 })),
        http.get("*/api/erp/settings/satis", () => HttpResponse.json({}, { status: 403 })),
        http.post("*/api/erp/invoices/preview", () => HttpResponse.json({}, { status: 400 })),
    );
}

function renderEditor() {
    return renderWithTheme(
        <SessionProvider>
            <OrderEditor id="so-1" />
        </SessionProvider>,
    );
}

beforeEach(() => resetNavigation("/satis/siparisler/so-1"));

describe("OrderEditor", () => {
    it("OrderEditor_MeasureLineComputesM2", async () => {
        setup(["satis.read", "satis.manage"]);
        server.use(http.get("*/api/erp/sales/orders/so-1", () => HttpResponse.json(orderDto())));

        const { user } = renderEditor();

        expect(await screen.findByText("3,68 m²")).toBeInTheDocument();
        const width = screen.getByRole("textbox", { name: "HALI01 en" });
        await user.clear(width);
        await user.type(width, "2");
        expect(screen.getByText("4,6 m²")).toBeInTheDocument();
    });

    it("OrderEditor_SupplyModeHiddenWithoutModule", async () => {
        setup(["satis.read", "satis.manage"]);
        server.use(http.get("*/api/erp/sales/orders/so-1", () => HttpResponse.json(orderDto())));

        renderEditor();

        expect(await screen.findByText("3,68 m²")).toBeInTheDocument();
        expect(screen.queryByRole("combobox", { name: "HALI01 tedarik şekli" })).not.toBeInTheDocument();
        expect(screen.queryByText("Tedarik")).not.toBeInTheDocument();
    });

    it("OrderEditor_SupplyModeShownWithModule", async () => {
        setup(["satis.read", "satis.manage"], ["siparise-gore-tedarik"]);
        server.use(http.get("*/api/erp/sales/orders/so-1", () => HttpResponse.json(orderDto({ lines: [orderLine({ supplyMode: "backToBack" })] }))));

        renderEditor();

        expect(await screen.findByRole("combobox", { name: "HALI01 tedarik şekli" })).toHaveValue("backToBack");
    });

    it("ProfitColumnHiddenWithoutPermission", async () => {
        setup(["satis.read", "satis.manage"]);
        server.use(http.get("*/api/erp/sales/orders/so-1", () => HttpResponse.json(orderDto({ lines: [orderLine({ estimatedUnitCost: 900, marginPercent: 55 })] }))));

        renderEditor();

        expect(await screen.findByText("3,68 m²")).toBeInTheDocument();
        expect(screen.queryByText("Marj")).not.toBeInTheDocument();
        expect(screen.queryByText(/% 55/)).not.toBeInTheDocument();
    });

    it("ProfitColumnShownWithMaliyetRead", async () => {
        setup(["satis.read", "satis.manage", "maliyet.read"]);
        server.use(http.get("*/api/erp/sales/orders/so-1", () => HttpResponse.json(orderDto({ lines: [orderLine({ estimatedUnitCost: 900, marginPercent: 55 })] }))));

        renderEditor();

        expect(await screen.findByText("Marj")).toBeInTheDocument();
        expect(screen.getByText(/% 55/)).toBeInTheDocument();
    });

    it("OrderDetail_ApprovedOrder_ShowsActionsAndTimeline", async () => {
        setup(["satis.read", "satis.manage", "satis.ship", "fatura.issue"]);
        server.use(
            http.get("*/api/erp/sales/orders/so-1", () =>
                HttpResponse.json(
                    orderDto({
                        status: "approved",
                        lines: [
                            orderLine({
                                supplyMode: "backToBack",
                                timeline: [
                                    { key: "ordered", label: "Tedarikçiye sipariş verildi", state: "done", at: null, note: "SA2026000001" },
                                    { key: "in-transit", label: "Kargoda", state: "current", at: null, note: "İrsaliye 778" },
                                ],
                            }),
                        ],
                    }),
                ),
            ),
        );

        renderEditor();

        expect(await screen.findByRole("button", { name: "Sevk et" })).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Hızlı fatura" })).toBeInTheDocument();
        expect(screen.getByText("İrsaliye 778")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Onayla" })).not.toBeInTheDocument();
    });
});
