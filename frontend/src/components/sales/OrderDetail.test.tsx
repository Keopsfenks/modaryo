import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import OrderDetail from "./OrderDetail";
import { orderDto } from "./salesFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

beforeEach(() => resetNavigation("/satis/siparisler/so-1"));

describe("OrderDetail", () => {
    it("OrderDetail_InvoiceFromShipments_CreatesDraftAndLinksShipments", async () => {
        const posted: { shipmentIds: string[] }[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["satis.read", "satis.manage", "fatura.manage"] }))),
            http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
            http.post("*/api/erp/sales/shipments/invoice-draft", async ({ request }) => {
                posted.push((await request.json()) as { shipmentIds: string[] });
                return HttpResponse.json({ id: "inv-9" }, { status: 201 });
            }),
        );
        const order = orderDto({
            status: "shipped",
            shipments: [
                { id: "sv-1", number: "SV2026000001", status: "shipped", date: "2026-11-25" },
                { id: "sv-2", number: "SV2026000002", status: "cancelled", date: "2026-11-26" },
            ],
        });

        const { user } = renderWithTheme(
            <SessionProvider>
                <OrderDetail order={order} planEditable={false} onChanged={() => undefined} />
            </SessionProvider>,
        );

        // Sevkler sekmesindeki numaralar sevk detayına bağlanır.
        await user.click(await screen.findByRole("tab", { name: /Sevkler/ }));
        expect(screen.getByRole("link", { name: "SV2026000001" })).toHaveAttribute("href", "/satis/sevkler/sv-1");

        await user.click(await screen.findByRole("button", { name: "Sevklerden fatura" }));
        // İptal edilmiş sevk listelenmez.
        expect(screen.getByRole("checkbox", { name: /SV2026000001/ })).toBeChecked();
        expect(screen.queryByRole("checkbox", { name: /SV2026000002/ })).not.toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Taslak oluştur" }));

        await waitFor(() => expect(posted).toEqual([{ shipmentIds: ["sv-1"], date: null }]));
        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/satis/faturalar/inv-9"));
    });
});
