import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ImportFileDetail from "./ImportFileDetail";
import ImportFiles from "./ImportFiles";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const file = {
    id: "if-1",
    number: "IT20260001",
    status: "open",
    supplierId: "s-1",
    supplierName: "Akın Tekstil",
    originCountry: "Çin",
    incoterm: "fob",
    currency: "USD",
    notes: null,
    links: [
        { id: "l-1", kind: "purchaseOrder", refId: "po-1", label: "SA2026000001" },
        { id: "l-2", kind: "goodsReceipt", refId: "r-1", label: "MK2026000001" },
    ],
};

const summary = {
    importFileId: "if-1",
    goodsValueTl: 10000,
    costsByType: [{ costType: "navlun", amountTl: 500 }],
    totalCostTl: 500,
    landedTotalTl: 10500,
    items: [{ productId: "p-1", productCode: "U00001", productName: "Tişört", variantId: null, baseQuantity: 100, goodsValueTl: 10000, allocatedCostTl: 500, landedUnitCostTl: 105 }],
};

function session(permissions: string[], modules: string[]) {
    server.use(http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions, modules }))));
}

beforeEach(() => resetNavigation("/satin-alma/ithalat/if-1"));

describe("ImportFileDetail", () => {
    it("ImportFile_HiddenWithoutModule", async () => {
        session(["satinalma.read", "fiyat.alis", "satinalma.manage"], []);
        const calls: string[] = [];
        server.use(
            http.get("*/api/erp/purchasing/import-files", () => {
                calls.push("list");
                return HttpResponse.json([]);
            }),
            http.get("*/api/erp/purchasing/import-files/if-1", () => {
                calls.push("get");
                return HttpResponse.json(file);
            }),
        );

        renderWithTheme(
            <SessionProvider>
                <ImportFileDetail id="if-1" />
                <ImportFiles />
            </SessionProvider>,
        );

        expect(await screen.findAllByText(/İthalat modülü firmanızda kapalı/)).toHaveLength(2);
        expect(calls).toEqual([]);
        expect(screen.queryByText("IT20260001")).not.toBeInTheDocument();
    });

    it("ImportFile_ShowsHeaderTimelineAndSummary", async () => {
        session(["satinalma.read", "fiyat.alis", "satinalma.manage"], ["ithalat"]);
        server.use(
            http.get("*/api/erp/purchasing/import-files/if-1", () => HttpResponse.json(file)),
            http.get("*/api/erp/purchasing/import-files/if-1/summary", () => HttpResponse.json(summary)),
        );

        renderWithTheme(
            <SessionProvider>
                <ImportFileDetail id="if-1" />
            </SessionProvider>,
        );

        expect(await screen.findByText("IT20260001")).toBeInTheDocument();
        expect(screen.getByText("FOB")).toBeInTheDocument();
        expect(screen.getByText("Çin")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /SA2026000001/ })).toHaveAttribute("href", "/satin-alma/siparisler/po-1");
        expect(await screen.findByText("105,00 ₺")).toBeInTheDocument();
        expect(screen.getByText(/Toplam maliyet: 10.500,00 ₺/)).toBeInTheDocument();
    });
});
