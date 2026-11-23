import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SettingsIndex from "./SettingsIndex";
import SettingsNav from "./SettingsNav";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";

function renderWith(ui: React.ReactElement, overrides: Parameters<typeof sessionInfo>[0]) {
    server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo(overrides))));
    return renderWithTheme(<SessionProvider>{ui}</SessionProvider>);
}

function mockMatchMedia(matches: boolean) {
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
        matches,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
    }));
}

describe("Settings navigation", () => {
    beforeEach(() => resetNavigation("/ayarlar/roller"));
    afterEach(() => {
        // @ts-expect-error matchMedia testte tanımlandı; jsdom varsayılanına dön.
        delete window.matchMedia;
    });

    it("SettingsNav_ShowsOnlyPermittedCategories", async () => {
        renderWith(<SettingsNav />, { permissions: ["roller.read", "members.read"] });

        expect(await screen.findByRole("link", { name: /Roller ve İzinler/ })).toHaveAttribute("href", "/ayarlar/roller");
        expect(screen.getByRole("link", { name: /Kullanıcılar/ })).toBeInTheDocument();
        expect(screen.queryByRole("link", { name: /Firma Bilgileri/ })).not.toBeInTheDocument();
        expect(screen.queryByRole("link", { name: /Denetim/ })).not.toBeInTheDocument();
    });

    it("SettingsNav_GroupsCategories", async () => {
        renderWith(<SettingsNav />, { permissions: ["*"] });

        const nav = await screen.findByRole("navigation", { name: "Ayarlar" });
        const group = within(nav).getByRole("group", { name: "Kullanıcılar" });
        expect(within(group).getByRole("link", { name: /Roller ve İzinler/ })).toBeInTheDocument();
        expect(within(group).getByRole("link", { name: /^Kullanıcılar/ })).toBeInTheDocument();
        expect(within(group).queryByRole("link", { name: /Denetim/ })).not.toBeInTheDocument();
        expect(within(nav).getByRole("group", { name: "Sistem" })).toBeInTheDocument();
    });

    it("SettingsNav_MobileRendersSelect", async () => {
        mockMatchMedia(true);
        renderWith(<SettingsNav />, { permissions: ["*"] });

        expect(await screen.findByRole("combobox", { name: "Ayar kategorisi" })).toBeInTheDocument();
        expect(screen.queryByRole("link", { name: /Firma Bilgileri/ })).not.toBeInTheDocument();
    });

    it("SettingsIndex_RedirectsToFirstVisible", async () => {
        renderWith(<SettingsIndex />, { permissions: ["roller.read", "denetim.read"] });

        await vi.waitFor(() => expect(router.replace).toHaveBeenCalledWith("/ayarlar/roller"));
    });

    it("SettingsIndex_NoVisibleCategory_ShowsNoAccess", async () => {
        renderWith(<SettingsIndex />, { permissions: ["cari.read"] });

        expect(await screen.findByText("Bu sayfayı görüntüleme yetkiniz yok.")).toBeInTheDocument();
        expect(router.replace).not.toHaveBeenCalled();
    });
});
