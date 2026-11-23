import { describe, expect, it, vi } from "vitest";
import { permanentRedirect } from "next/navigation";
import Page from "./page";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

describe("Eski üyeler yolu", () => {
    it("OldMembersPath_Redirects", () => {
        Page();

        expect(permanentRedirect).toHaveBeenCalledWith("/ayarlar/kullanicilar");
    });
});
