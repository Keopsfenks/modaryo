import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider, useSession } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ModulesForm from "./ModulesForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const MODULES = "*/api/identity/companies/current/modules";

const catalog = (posEnabled: boolean) => [
    { key: "pos", name: "Perakende Satış (POS)", description: "Kasada hızlı satış.", wave: 1, enabled: posEnabled },
    { key: "crm", name: "Müşteri İlişkileri (CRM)", description: "Fırsat takibi.", wave: 2, enabled: false },
];

function Probe() {
    const { hasModule } = useSession();
    return <p>{hasModule("pos") ? "POS menüde" : "POS menüde değil"}</p>;
}

describe("ModulesForm", () => {
    it("ToggleModule_RefreshesSessionAndSidebar", async () => {
        let posOn = false;
        let put: unknown;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo({ modules: posOn ? ["pos"] : [] }))),
            http.get(MODULES, () => HttpResponse.json(catalog(posOn))),
            http.put(MODULES, async ({ request }) => {
                put = await request.json();
                posOn = true;
                return HttpResponse.json(catalog(true));
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <Probe />
                <ModulesForm />
            </SessionProvider>,
        );

        expect(await screen.findByText("POS menüde değil")).toBeInTheDocument();
        await user.click(await screen.findByRole("switch", { name: "Perakende Satış (POS)" }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Modüller kaydedildi.")).toBeInTheDocument();
        expect(put).toEqual({ enabled: ["pos"] });
        expect(await screen.findByText("POS menüde")).toBeInTheDocument();
    });

    it("DisablingModule_AsksConfirmation", async () => {
        let put = 0;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo({ modules: ["pos"] }))),
            http.get(MODULES, () => HttpResponse.json(catalog(true))),
            http.put(MODULES, () => {
                put++;
                return HttpResponse.json(catalog(false));
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <ModulesForm />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("switch", { name: "Perakende Satış (POS)" }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Kapatılan modülün ekranları gizlenir; veriler silinmez.")).toBeInTheDocument();
        expect(put).toBe(0);

        await user.click(screen.getByRole("button", { name: "Kapat ve kaydet" }));
        await waitFor(() => expect(put).toBe(1));
    });
});
