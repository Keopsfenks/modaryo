import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { IconCash, IconLayoutDashboard } from "@tabler/icons-react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import SidebarContent from "./SidebarContent";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));
vi.mock("@/config/navigation", async (importOriginal) => {
    const original = await importOriginal<typeof import("@/config/navigation")>();
    return {
        ...original,
        navigation: [
            {
                title: "Test",
                items: [
                    { title: "Panel", href: "/panel", icon: IconLayoutDashboard },
                    { title: "Kasa Satış", href: "/pos", icon: IconCash, module: "pos" },
                ],
            },
            ...original.navigation.filter((s) => s.items.some((i) => i.href === "/ayarlar")),
        ],
    };
});

const SESSION = "http://localhost:3000/api/identity/auth/session";

function renderSidebar(overrides: Parameters<typeof sessionInfo>[0]) {
    server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo(overrides))));
    return renderWithTheme(
        <SessionProvider>
            <SidebarContent />
        </SessionProvider>,
    );
}

describe("SidebarContent", () => {
    beforeEach(() => resetNavigation("/panel"));

    it("Sidebar_HidesItemWhoseModuleIsDisabled", async () => {
        renderSidebar({ modules: [] });

        expect(await screen.findByRole("link", { name: "Panel" })).toBeInTheDocument();
        expect(screen.queryByRole("link", { name: "Kasa Satış" })).not.toBeInTheDocument();
    });

    it("Sidebar_ShowsItemWhoseModuleIsEnabled", async () => {
        renderSidebar({ modules: ["pos"] });

        expect(await screen.findByRole("link", { name: "Kasa Satış" })).toBeInTheDocument();
    });

    it("Sidebar_ShowsAyarlarWhenAnyCategoryVisible", async () => {
        renderSidebar({ permissions: ["roller.read"] });

        expect(await screen.findByRole("link", { name: "Ayarlar" })).toHaveAttribute("href", "/ayarlar");
    });

    it("Sidebar_HidesAyarlarForPlainMember", async () => {
        renderSidebar({ permissions: ["cari.read"] });

        await screen.findByRole("link", { name: "Panel" });
        expect(screen.queryByRole("link", { name: "Ayarlar" })).not.toBeInTheDocument();
    });
});
