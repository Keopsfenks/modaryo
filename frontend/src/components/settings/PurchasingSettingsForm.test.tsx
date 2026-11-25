import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import PurchasingSettingsForm from "./PurchasingSettingsForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const wh = (id: string, branchId: string, name: string, kind: string) => ({ id, branchId, code: id, name, kind, isDefault: false, isActive: true, inUse: false });

type Saved = { version: number; value: { quarantineEnabled: boolean; quarantineWarehouseByBranch: Record<string, string>; defaultLandedKey: string } };

describe("PurchasingSettingsForm", () => {
    it("Settings_QuarantineWarehousePerBranch", async () => {
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["ayarlar.satinalma", "stok.read"] }))),
            http.get(`${IDENTITY}/companies/current/branches`, () =>
                HttpResponse.json([
                    { id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true },
                    { id: "b-2", code: "IZM", name: "İzmir", isActive: true, isDefault: false },
                ]),
            ),
            http.get("*/api/erp/inventory/warehouses", () =>
                HttpResponse.json([wh("w-q1", "b-1", "Merkez karantina", "quarantine"), wh("w-n1", "b-1", "Merkez ana", "normal"), wh("w-q2", "b-2", "İzmir karantina", "quarantine")]),
            ),
            http.get("*/api/erp/settings/satinalma", () =>
                HttpResponse.json({ version: 3, value: { overDeliveryPercent: 0, priceVariancePercent: 2, quarantineEnabled: false, quarantineWarehouseByBranch: {}, defaultLandedKey: "amount" } }),
            ),
        );
        let put: Saved | null = null;
        server.use(
            http.put("*/api/erp/settings/satinalma", async ({ request }) => {
                put = (await request.json()) as Saved;
                return HttpResponse.json({ version: 4, value: put.value });
            }),
        );

        const { user } = renderWithTheme(
            <SessionProvider>
                <PurchasingSettingsForm />
            </SessionProvider>,
        );

        expect(screen.queryByRole("combobox", { name: /karantina deposu/i })).not.toBeInTheDocument();
        await user.click(await screen.findByRole("switch", { name: "Karantinayı etkinleştir" }));
        const merkez = await screen.findByRole("combobox", { name: "Merkez karantina deposu" });
        const izmir = screen.getByRole("combobox", { name: "İzmir karantina deposu" });
        // Yalnızca o şubenin karantina türü depoları listelenir.
        expect(Array.from((merkez as HTMLSelectElement).options).map((o) => o.text)).toEqual(["Seçin", "Merkez karantina"]);
        await user.selectOptions(merkez, "w-q1");
        await user.selectOptions(izmir, "w-q2");
        await user.selectOptions(screen.getByRole("combobox", { name: "Varsayılan dağıtım anahtarı" }), "weight");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(put).not.toBeNull());
        const sent = put as unknown as Saved;
        expect(sent.version).toBe(3);
        expect(sent.value.quarantineEnabled).toBe(true);
        expect(sent.value.quarantineWarehouseByBranch).toEqual({ "b-1": "w-q1", "b-2": "w-q2" });
        expect(sent.value.defaultLandedKey).toBe("weight");
    });
});
