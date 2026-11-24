import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import StockSettingsForm from "./StockSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(total: number) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.stok", "stok.read"] }))),
        http.get("*/api/erp/settings/stok", () => HttpResponse.json({ version: 1, value: { method: "weightedAverage", allowNegative: false } })),
        http.get("*/api/erp/inventory/balances", () => HttpResponse.json({ items: [], total })),
    );
    return renderWithTheme(
        <SessionProvider>
            <StockSettingsForm />
        </SessionProvider>,
    );
}

describe("StockSettingsForm", () => {
    it("StockSettings_MethodLockedExplains", async () => {
        setup(3);
        expect(await screen.findByText(/stok hareketi bulunduğu için maliyet yöntemi değiştirilemez/)).toBeInTheDocument();
        expect(screen.getByRole("radio", { name: /Ağırlıklı ortalama/ })).toBeDisabled();
        expect(screen.getByRole("radio", { name: /FIFO/ })).toBeDisabled();
        expect(screen.getByRole("radio", { name: /Ağırlıklı ortalama/ })).toBeChecked();
    });

    it("StockSettings_MethodEditableWithoutMovements", async () => {
        setup(0);
        const fifo = await screen.findByRole("radio", { name: /FIFO/ });
        expect(fifo).toBeEnabled();
        expect(screen.queryByText(/stok hareketi bulunduğu için/)).not.toBeInTheDocument();
    });
});
