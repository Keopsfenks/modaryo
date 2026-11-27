import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import type { InvoiceDto } from "@/lib/api/invoicing";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ReturnForm from "./ReturnForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const wh = (id: string, name: string, kind: string, isDefault = false) => ({ id, branchId: "b-1", code: id, name, kind, isDefault, isActive: true, inUse: false });

const invoice = {
    id: "inv-1",
    kind: "sales",
    status: "issued",
    number: "SF2026000001",
    branchId: "b-1",
    currentAccountId: "acc-1",
    currentAccountName: "Akın Tekstil",
    lines: [{ id: "il-1", productId: "prod-1", variantId: null, description: "Yün halı", unit: "m2", factor: 1, quantity: 3.68, unitPrice: 2000, vatIncluded: true, vatRate: 20, net: 6133.33, total: 7360 }],
} as unknown as InvoiceDto;

type Posted = { invoiceId: string; warehouseKind: string; warehouseId: string; reason: string | null; lines: { invoiceLineId: string; quantity: number }[] };

describe("ReturnForm", () => {
    it("ReturnForm_QuarantineOption", async () => {
        const posted: Posted[] = [];
        server.use(
            http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([wh("w-n", "Ana depo", "normal", true), wh("w-q", "Karantina deposu", "quarantine"), wh("w-d", "Hasarlı deposu", "damaged")])),
            http.get("*/api/erp/catalog/products/*", () => HttpResponse.json({}, { status: 404 })),
            http.post("*/api/erp/sales/returns", async ({ request }) => {
                posted.push((await request.json()) as Posted);
                return HttpResponse.json({ id: "r-1", number: "IA2026000001", returnInvoiceId: "inv-2", lines: [] }, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<ReturnForm invoice={invoice} onClose={() => undefined} onDone={() => undefined} />);

        const warehouse = await screen.findByRole("combobox", { name: "Hedef depo" });
        await waitFor(() => expect(Array.from((warehouse as HTMLSelectElement).options).map((o) => o.text)).toEqual(["Ana depo"]));

        await user.selectOptions(screen.getByRole("combobox", { name: "İade türü" }), "quarantine");
        expect(Array.from((warehouse as HTMLSelectElement).options).map((o) => o.text)).toEqual(["Karantina deposu"]);

        await user.type(screen.getByRole("textbox", { name: "Yün halı iade miktarı" }), "1,5");
        await user.click(screen.getByRole("button", { name: "İade al" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ invoiceId: "inv-1", warehouseKind: "quarantine", warehouseId: "w-q", lines: [{ invoiceLineId: "il-1", quantity: 1.5 }] });
        expect(await screen.findByText(/İade kaydedildi/)).toBeInTheDocument();
        expect(screen.getByText("İade faturası taslağını aç")).toHaveAttribute("href", "/satis/faturalar/inv-2");
    });

    it("ReturnForm_ExceedsInvoicedBlocked", async () => {
        const posted: unknown[] = [];
        server.use(
            http.get("*/api/erp/inventory/warehouses", () => HttpResponse.json([wh("w-n", "Ana depo", "normal", true)])),
            http.get("*/api/erp/catalog/products/*", () => HttpResponse.json({}, { status: 404 })),
            http.post("*/api/erp/sales/returns", async ({ request }) => {
                posted.push(await request.json());
                return HttpResponse.json({}, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<ReturnForm invoice={invoice} onClose={() => undefined} onDone={() => undefined} />);

        await screen.findByRole("option", { name: "Ana depo" });
        await user.type(screen.getByRole("textbox", { name: "Yün halı iade miktarı" }), "4");
        await user.click(screen.getByRole("button", { name: "İade al" }));

        expect(await screen.findByText(/İade miktarı faturalanan miktarı aşıyor/)).toBeInTheDocument();
        expect(posted).toHaveLength(0);
    });
});
