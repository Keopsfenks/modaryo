import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { SetupStep } from "@/config/setupSteps";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SetupWizard from "./SetupWizard";
import DoneStep from "./steps/DoneStep";
import SectorStep from "./steps/SectorStep";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const MODULES = "*/api/identity/companies/current/modules";
const SETUP = "*/api/erp/settings/kurulum";

const catalog = (enabled: string[]) =>
    ["pos", "seri-lot", "cek-senet"].map((key) => ({ key, name: `Modül ${key}`, description: "", wave: 1, enabled: enabled.includes(key) }));

beforeEach(() => resetNavigation("/kurulum"));

describe("SetupWizard", () => {
    it("Wizard_StepsOrderedByOrderField", async () => {
        const fake = (title: string) =>
            function Step() {
                return <p>{title}</p>;
            };
        const steps: SetupStep[] = [
            { key: "c", title: "Üçüncü", order: 30, Component: fake("içerik-3") },
            { key: "a", title: "Birinci", order: 10, Component: fake("içerik-1") },
            { key: "b", title: "İkinci", order: 20, Component: fake("içerik-2") },
        ];
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo())));
        const { user } = renderWithTheme(
            <SessionProvider>
                <SetupWizard steps={steps} />
            </SessionProvider>,
        );

        expect(await screen.findByText("içerik-1")).toBeInTheDocument();
        const labels = screen.getAllByTestId("setup-step-label").map((e) => e.textContent);
        expect(labels).toEqual(["Birinci", "İkinci", "Üçüncü"]);
        await user.click(screen.getByRole("button", { name: "İleri" }));
        expect(await screen.findByText("içerik-2")).toBeInTheDocument();
    });

    it("Wizard_SectorPerakende_EnablesPosAndSeriLot", async () => {
        let enabledPut: unknown;
        let setupPut: { value: { sector: string } } | undefined;
        server.use(
            http.get(MODULES, () => HttpResponse.json(catalog(["cek-senet"]))),
            http.put(MODULES, async ({ request }) => {
                enabledPut = await request.json();
                return HttpResponse.json(catalog(["pos", "seri-lot", "cek-senet"]));
            }),
            http.get(SETUP, () => HttpResponse.json({ version: 0, value: { completedAt: null, dismissedAt: null, sector: null } })),
            http.put(SETUP, async ({ request }) => {
                setupPut = (await request.json()) as typeof setupPut;
                return HttpResponse.json({ version: 1, value: setupPut!.value });
            }),
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
        );
        const onNext = vi.fn();
        const { user } = renderWithTheme(
            <SessionProvider>
                <SectorStep onNext={onNext} onBack={() => {}} />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("radio", { name: /Perakende/ }));
        expect(screen.getByText("Modül pos")).toBeInTheDocument();
        expect(screen.getByText("Modül seri-lot")).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Devam" }));

        await waitFor(() => expect(onNext).toHaveBeenCalled());
        expect((enabledPut as { enabled: string[] }).enabled.slice().sort()).toEqual(["cek-senet", "pos", "seri-lot"]);
        expect(setupPut?.value.sector).toBe("perakende");
    });

    it("Wizard_Done_SetsCompletedAtAndGoesToPanel", async () => {
        let setupPut: { value: { completedAt: string | null } } | undefined;
        server.use(
            http.get(SETUP, () => HttpResponse.json({ version: 2, value: { completedAt: null, dismissedAt: null, sector: "hizmet" } })),
            http.put(SETUP, async ({ request }) => {
                setupPut = (await request.json()) as typeof setupPut;
                return HttpResponse.json({ version: 3, value: setupPut!.value });
            }),
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <DoneStep onNext={() => {}} onBack={() => {}} />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("button", { name: "Panele git" }));

        await waitFor(() => expect(router.push).toHaveBeenCalledWith("/panel"));
        expect(setupPut?.value.completedAt).toEqual(expect.any(String));
    });
});
