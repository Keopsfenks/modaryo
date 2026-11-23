import { NextRequest } from "next/server";
import { describe, expect, it } from "vitest";
import { proxy } from "./proxy";

function request(path: string, withCookie = false) {
    const req = new NextRequest(`http://localhost:3000${path}`);
    if (withCookie) {
        req.cookies.set("modaryo_sid", "abc");
    }
    return req;
}

describe("proxy", () => {
    it("Proxy_NoCookie_ProtectedPath_RedirectsToLoginWithNext", () => {
        const response = proxy(request("/hesap?sekme=oturumlar"));

        expect(response.status).toBe(307);
        expect(new URL(response.headers.get("location")!).pathname + new URL(response.headers.get("location")!).search)
            .toBe("/giris?next=%2Fhesap%3Fsekme%3Doturumlar");
    });

    it("Proxy_WithCookie_Passes", () => {
        const response = proxy(request("/hesap", true));

        expect(response.headers.get("location")).toBeNull();
        expect(response.headers.get("x-middleware-next")).toBe("1");
    });

    it("Proxy_PublicPath_Passes", () => {
        const response = proxy(request("/davet/abc"));

        expect(response.headers.get("location")).toBeNull();
    });

    it("Proxy_Baslangic_WithoutCookie_Redirects", () => {
        const response = proxy(request("/baslangic"));

        expect(response.status).toBe(307);
    });
});

describe("proxy / herkese açık site", () => {
    it.each(["/", "/ozellikler/satis", "/ozellikler", "/cozumler/hali-ev-tekstili", "/fiyatlar", "/demo", "/iletisim", "/kvkk", "/blog/ilk-yazi", "/yardim", "/sitemap.xml", "/robots.txt"])(
        "Proxy_PublicSiteRoutesAllowed %s",
        (path) => {
            const response = proxy(request(path));

            expect(response.headers.get("location")).toBeNull();
            expect(response.headers.get("x-middleware-next")).toBe("1");
        },
    );

    it.each(["/panel", "/ayarlar", "/cari", "/ozellikleri-gizle"])("Proxy_PanelStillProtected %s", (path) => {
        const response = proxy(request(path));

        expect(response.status).toBe(307);
        expect(new URL(response.headers.get("location")!).pathname).toBe("/giris");
        expect(new URL(response.headers.get("location")!).searchParams.get("next")).toBe(path);
    });
});
