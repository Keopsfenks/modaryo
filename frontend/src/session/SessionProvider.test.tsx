import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiFetch } from "@/lib/api/client";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import { SessionProvider, useSession } from "./SessionProvider";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";

function Probe() {
    const { session, hasPermission } = useSession();
    return (
        <div>
            <span>kullanıcı: {session.user.fullName}</span>
            <span>yonet: {String(hasPermission("members.manage"))}</span>
        </div>
    );
}

function AccessProbe() {
    const s = useSession();
    return <p>{[s.hasModule("pos"), s.hasModule("cek-senet"), s.canAccessBranch("b1"), s.canAccessBranch("b2")].join(",")}</p>;
}

describe("SessionProvider", () => {
    beforeEach(() => {
        resetNavigation("/hesap", "x=1");
        window.history.pushState({}, "", "/hesap?x=1");
    });

    it("SessionProvider_RendersChildrenWithSession", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo())));

        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);

        expect(await screen.findByText("kullanıcı: Ali Veli")).toBeInTheDocument();
    });

    it("SessionProvider_On401_RedirectsToLoginWithNext", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(problem(401, "Unauthorized"), { status: 401 })));

        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/giris?next=%2Fhesap%3Fx%3D1"));
        expect(screen.queryByText(/kullanıcı:/)).not.toBeInTheDocument();
    });

    it("SessionProvider_OnLaterApi401_RedirectsToLogin", async () => {
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.get("http://localhost:3000/api/erp/me", () => HttpResponse.json(problem(401, "Unauthorized"), { status: 401 })),
        );
        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);
        await screen.findByText("kullanıcı: Ali Veli");

        await apiFetch("/api/erp/me").catch(() => undefined);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/giris?next=%2Fhesap%3Fx%3D1"));
    });

    it("SessionProvider_NoCompany_RedirectsToBaslangic", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ activeCompany: null, companies: [], permissions: [] }))));

        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/baslangic"));
    });

    it("SessionProvider_NoCompany_AllowedWhenNotRequired", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ activeCompany: null, companies: [], permissions: [] }))));

        renderWithTheme(<SessionProvider requireCompany={false}><Probe /></SessionProvider>);

        expect(await screen.findByText("kullanıcı: Ali Veli")).toBeInTheDocument();
        expect(router.replace).not.toHaveBeenCalled();
    });

    it("HasPermission_StarGrantsAll", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions: ["*"] }))));

        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);

        expect(await screen.findByText("yonet: true")).toBeInTheDocument();
    });

    it("HasPermission_ExactOnly", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ permissions: ["members.read"] }))));

        renderWithTheme(<SessionProvider><Probe /></SessionProvider>);

        expect(await screen.findByText("yonet: false")).toBeInTheDocument();
    });

    it("Session_HasModuleAndCanAccessBranch", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ modules: ["pos"], branchIds: ["b1"] }))));
        renderWithTheme(<SessionProvider><AccessProbe /></SessionProvider>);

        expect(await screen.findByText("true,false,true,false")).toBeInTheDocument();
    });

    it("Session_NullBranchIds_AllowsEveryBranch", async () => {
        server.use(http.get(SESSION, () => HttpResponse.json(sessionInfo({ branchIds: null }))));
        renderWithTheme(<SessionProvider><AccessProbe /></SessionProvider>);

        expect(await screen.findByText("false,false,true,true")).toBeInTheDocument();
    });
});
