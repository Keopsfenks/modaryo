import { describe, expect, it } from "vitest";
import { afterSignInPath, isPublicPath, loginUrl, safeNext } from "./navigation";

describe("safeNext", () => {
    it.each([
        ["/hesap", "/hesap"],
        ["/firma/uyeler?x=1", "/firma/uyeler?x=1"],
        ["https://kotu.site", "/"],
        ["//kotu.site", "/"],
        ["/\\kotu.site", "/"],
        ["javascript:alert(1)", "/"],
        ["", "/"],
        [null, "/"],
        [undefined, "/"],
    ])("safeNext(%s) → %s", (input, expected) => {
        expect(safeNext(input)).toBe(expected);
    });
});

describe("isPublicPath", () => {
    it.each([
        ["/giris", true],
        ["/kayit", true],
        ["/sifremi-unuttum", true],
        ["/sifre-sifirla", true],
        ["/eposta-dogrula", true],
        ["/davet/abc", true],
        ["/hesap", false],
        ["/", false],
        ["/baslangic", false],
        ["/girisx", false],
    ])("isPublicPath(%s) → %s", (path, expected) => {
        expect(isPublicPath(path)).toBe(expected);
    });
});

describe("loginUrl", () => {
    it("encodes next", () => {
        expect(loginUrl("/hesap?x=1")).toBe("/giris?next=%2Fhesap%3Fx%3D1");
    });
});

describe("afterSignInPath", () => {
    const withCompany = { activeCompany: { id: "x" } };
    const withoutCompany = { activeCompany: null };

    it("with company uses safe next", () => {
        expect(afterSignInPath(withCompany, "/hesap")).toBe("/hesap");
        expect(afterSignInPath(withCompany, "https://kotu.site")).toBe("/");
    });

    it("without company goes to onboarding", () => {
        expect(afterSignInPath(withoutCompany, "/hesap")).toBe("/baslangic");
    });

    it("without company still returns to an invitation link", () => {
        expect(afterSignInPath(withoutCompany, "/davet/abc")).toBe("/davet/abc");
    });
});
