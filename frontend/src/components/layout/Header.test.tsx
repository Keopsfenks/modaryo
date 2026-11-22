import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { demoCompany, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import Header from "./Header";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const second = { id: "0193a6f3-1111-7c11-8e2b-6d9f4a3c5b21", name: "İkinci Firma", roleName: "Kullanıcı" };

function renderHeader() {
    return renderWithTheme(
        <SessionProvider>
            <Header onToggleSidebar={() => undefined} />
        </SessionProvider>,
    );
}

async function openProfileMenu(user: ReturnType<typeof renderWithTheme>["user"]) {
    await user.click(await screen.findByRole("button", { name: /profil menüsü/i }));
    return screen.findByRole("menu");
}

describe("Header", () => {
    beforeEach(() => resetNavigation("/"));

    it("Header_ShowsUserNameAndEmail", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo())));
        const { user } = renderHeader();

        const menu = await openProfileMenu(user);

        expect(within(menu).getByText("Ali Veli")).toBeInTheDocument();
        expect(within(menu).getByText("ali@ornek.com")).toBeInTheDocument();
        expect(within(menu).getByRole("menuitem", { name: /profilim/i })).toHaveAttribute("href", "/hesap");
    });

    it("Header_Logout_CallsApiAndRedirects", async () => {
        let loggedOut = false;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.post("http://localhost:3000/api/identity/auth/logout", () => {
                loggedOut = true;
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = renderHeader();

        const menu = await openProfileMenu(user);
        await user.click(within(menu).getByRole("menuitem", { name: /çıkış yap/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/giris"));
        expect(loggedOut).toBe(true);
    });

    it("Header_CompanySwitcher_OnlyWithMultipleCompanies", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo())));
        const { user } = renderHeader();

        const menu = await openProfileMenu(user);

        expect(within(menu).queryByText(/firma değiştir/i)).not.toBeInTheDocument();
    });

    it("Header_SwitchCompany_CallsApiAndRefreshes", async () => {
        let switchedTo: unknown;
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo({ companies: [demoCompany, second] }))),
            http.post("http://localhost:3000/api/identity/auth/session/tenant", async ({ request }) => {
                switchedTo = await request.json();
                return HttpResponse.json(sessionInfo({ activeCompany: second, companies: [demoCompany, second], permissions: ["members.read"] }));
            }),
        );
        const { user } = renderHeader();

        const menu = await openProfileMenu(user);
        expect(within(menu).getByText(/firma değiştir/i)).toBeInTheDocument();
        await user.click(within(menu).getByRole("menuitem", { name: /İkinci Firma/ }));

        await waitFor(() => expect(router.refresh).toHaveBeenCalled());
        expect(switchedTo).toEqual({ tenantId: second.id });
    });
});
