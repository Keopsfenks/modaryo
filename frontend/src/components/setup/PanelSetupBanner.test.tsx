import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SetupBanner from "./SetupBanner";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const SETUP = "*/api/erp/settings/kurulum";
const pending = { version: 0, value: { completedAt: null, dismissedAt: null, sector: null } };

beforeEach(() => resetNavigation("/panel"));

function renderBanner() {
    return renderWithTheme(
        <SessionProvider>
            <SetupBanner />
        </SessionProvider>,
    );
}

describe("SetupBanner", () => {
    it("Banner_ShownWhenSetupNotDone", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo())), http.get(SETUP, () => HttpResponse.json(pending)));
        renderBanner();

        expect(await screen.findByText("Firmanızı birkaç adımda kuralım")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: "Kuruluma başla" })).toHaveAttribute("href", "/kurulum");
    });

    it("Banner_HiddenWithoutFirmaPermission", async () => {
        let asked = false;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions: ["members.read"] }))),
            http.get(SETUP, () => {
                asked = true;
                return HttpResponse.json(pending);
            }),
        );
        renderBanner();

        await new Promise((r) => setTimeout(r, 100));
        expect(screen.queryByText("Firmanızı birkaç adımda kuralım")).not.toBeInTheDocument();
        expect(asked).toBe(false);
    });

    it("Banner_Dismiss_SetsDismissedAt", async () => {
        let put: { version: number; value: { dismissedAt: string | null } } | undefined;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.get(SETUP, () => HttpResponse.json(pending)),
            http.put(SETUP, async ({ request }) => {
                put = (await request.json()) as typeof put;
                return HttpResponse.json({ version: 1, value: put!.value });
            }),
        );
        const { user } = renderBanner();

        await user.click(await screen.findByRole("button", { name: "Daha sonra" }));

        await waitFor(() => expect(screen.queryByText("Firmanızı birkaç adımda kuralım")).not.toBeInTheDocument());
        expect(put?.version).toBe(0);
        expect(put?.value.dismissedAt).toEqual(expect.any(String));
    });
});
