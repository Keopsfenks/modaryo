import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import Warehouses from "./Warehouses";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const wh = (overrides: Record<string, unknown>) => ({
    id: "w-1",
    branchId: "b-1",
    code: "MRK-DEPO",
    name: "Merkez Depo",
    kind: "normal",
    isDefault: true,
    isActive: true,
    inUse: false,
    ...overrides,
});

describe("Warehouses", () => {
    it("Warehouses_DefaultCannotBeDeleted", async () => {
        const deleted: string[] = [];
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["stok.read", "stok.manage"] }))),
            http.get("*/api/identity/companies/current/branches", () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/inventory/warehouses", () =>
                HttpResponse.json([wh({}), wh({ id: "w-2", code: "HSR", name: "Hasarlı Depo", kind: "damaged", isDefault: false })]),
            ),
            http.delete("*/api/erp/inventory/warehouses/:id", ({ params }) => {
                deleted.push(String(params.id));
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <Warehouses />
            </SessionProvider>,
        );

        const defaultDelete = await screen.findByRole("button", { name: "Merkez Depo sil" });
        expect(defaultDelete).toBeDisabled();
        expect(screen.getByRole("button", { name: "Merkez Depo pasifleştir" })).toBeDisabled();
        expect(screen.getByText("Varsayılan")).toBeInTheDocument();

        const otherDelete = screen.getByRole("button", { name: "Hasarlı Depo sil" });
        expect(otherDelete).toBeEnabled();
        await user.click(otherDelete);
        await user.click(await screen.findByRole("button", { name: "Sil" }));
        expect(deleted).toEqual(["w-2"]);
    });
});
