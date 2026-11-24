import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { setupSteps } from "@/config/setupSteps";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import WarehouseStep from "./WarehouseStep";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";

describe("WarehouseStep", () => {
    it("WizardWarehouseStep_Renames", async () => {
        const puts: Record<string, unknown>[] = [];
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.get("*/api/identity/companies/current/branches", () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
            http.get("*/api/erp/inventory/warehouses/default", ({ request }) => {
                expect(new URL(request.url).searchParams.get("branchId")).toBe("b-1");
                return HttpResponse.json({ id: "w-1", branchId: "b-1", code: "MRK-DEPO", name: "Merkez Depo", kind: "normal", isDefault: true, isActive: true, inUse: false });
            }),
            http.put("*/api/erp/inventory/warehouses/w-1", async ({ request }) => {
                puts.push((await request.json()) as Record<string, unknown>);
                return HttpResponse.json({ id: "w-1" });
            }),
        );
        const onNext = vi.fn();
        const { user } = renderWithTheme(
            <SessionProvider>
                <WarehouseStep onNext={onNext} onBack={() => undefined} />
            </SessionProvider>,
        );

        const name = await screen.findByRole("textbox", { name: "Depo adı" });
        await waitFor(() => expect(name).toHaveValue("Merkez Depo"));
        await user.clear(name);
        await user.type(name, "Ana Depo");
        await user.click(screen.getByRole("button", { name: "Devam" }));

        await waitFor(() => expect(onNext).toHaveBeenCalled());
        expect(puts).toEqual([{ code: "MRK-DEPO", name: "Ana Depo", kind: "normal" }]);
        expect(screen.getByRole("button", { name: "Açılış stoklarını Excel'den yükle" })).toBeInTheDocument();
    });

    it("WizardWarehouseStep_IsRegisteredAtOrder30", () => {
        const step = setupSteps.find((s) => s.key === "warehouse");
        expect(step).toMatchObject({ order: 30, title: "İlk depo" });
    });
});
