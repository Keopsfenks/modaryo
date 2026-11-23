import { screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { renderWithTheme } from "@/test/render";
import MegaMenu from "./MegaMenu";
import SiteHeader from "./SiteHeader";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

describe("SiteHeader", () => {
    it("Header_WithoutSession_ShowsLoginAndSignup", () => {
        renderWithTheme(<SiteHeader hasSession={false} />);

        expect(screen.getByRole("link", { name: "Giriş yap" })).toHaveAttribute("href", "/giris");
        expect(screen.getByRole("link", { name: "Ücretsiz başla" })).toHaveAttribute("href", "/kayit");
        expect(screen.queryByRole("link", { name: "Panele git" })).not.toBeInTheDocument();
    });

    it("Header_WithSessionCookie_ShowsPanelButton", () => {
        renderWithTheme(<SiteHeader hasSession />);

        expect(screen.getByRole("link", { name: "Panele git" })).toHaveAttribute("href", "/panel");
        expect(screen.queryByRole("link", { name: "Giriş yap" })).not.toBeInTheDocument();
        expect(screen.queryByRole("link", { name: "Ücretsiz başla" })).not.toBeInTheDocument();
    });

    it("Header_PricesIsPlainLink_AndMenusExist", () => {
        renderWithTheme(<SiteHeader hasSession={false} />);

        expect(screen.getByRole("link", { name: "Fiyatlar" })).toHaveAttribute("href", "/fiyatlar");
        for (const name of ["Özellikler", "Çözümler", "Kaynaklar"]) {
            expect(screen.getByRole("button", { name })).toBeInTheDocument();
        }
    });
});

describe("MegaMenu", () => {
    const groups = [{ title: "Finans", items: [{ href: "/ozellikler/e-fatura", label: "E-Fatura", summary: "Özet" }] }];

    it("MegaMenu_KeyboardOpensAndEscapeCloses", async () => {
        const { user } = renderWithTheme(<MegaMenu label="Özellikler" groups={groups} />);
        const trigger = screen.getByRole("button", { name: "Özellikler" });

        expect(trigger).toHaveAttribute("aria-expanded", "false");
        trigger.focus();
        await user.keyboard("{Enter}");

        const panel = await screen.findByRole("dialog", { name: "Özellikler" });
        expect(trigger).toHaveAttribute("aria-expanded", "true");
        expect(within(panel).getByRole("link", { name: /E-Fatura/ })).toHaveAttribute("href", "/ozellikler/e-fatura");

        await user.keyboard("{Escape}");
        expect(screen.queryByRole("dialog", { name: "Özellikler" })).not.toBeInTheDocument();
        expect(trigger).toHaveAttribute("aria-expanded", "false");
    });

    it("MegaMenu_Space_Opens", async () => {
        const { user } = renderWithTheme(<MegaMenu label="Özellikler" groups={groups} />);
        screen.getByRole("button", { name: "Özellikler" }).focus();
        await user.keyboard(" ");

        expect(await screen.findByRole("dialog", { name: "Özellikler" })).toBeInTheDocument();
    });
});
