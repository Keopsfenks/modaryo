import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SettingsPage from "./SettingsPage";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";

function renderPage(permissions: string[]) {
    server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions }))));
    return renderWithTheme(
        <SessionProvider>
            <SettingsPage category="roller" title="Roller" description="Rolleri yönetin.">
                <p>Rol formu</p>
            </SettingsPage>
        </SessionProvider>,
    );
}

describe("SettingsPage", () => {
    it("SettingsPage_WithPermission_RendersChildren", async () => {
        renderPage(["roller.read"]);

        expect(await screen.findByText("Rol formu")).toBeInTheDocument();
        expect(screen.getByRole("heading", { name: "Roller" })).toBeInTheDocument();
    });

    it("SettingsPage_WithoutPermission_ShowsNoAccess", async () => {
        renderPage(["members.read"]);

        expect(await screen.findByText("Bu sayfayı görüntüleme yetkiniz yok.")).toBeInTheDocument();
        expect(screen.queryByText("Rol formu")).not.toBeInTheDocument();
    });
});
