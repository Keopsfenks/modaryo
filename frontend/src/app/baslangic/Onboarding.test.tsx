import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { demoCompany, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import Onboarding from "./Onboarding";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const API = "http://localhost:3000/api/identity";
const noCompany = sessionInfo({ activeCompany: null, companies: [], permissions: [] });
const invitation = { id: "inv-1", companyName: "Akdeniz Tekstil", invitedBy: "Sahip Kişi", roleName: "Kullanıcı", expiresAt: "2026-11-28T00:00:00Z" };

function renderOnboarding() {
    return renderWithTheme(
        <SessionProvider requireCompany={false}>
            <Onboarding />
        </SessionProvider>,
    );
}

describe("Onboarding", () => {
    beforeEach(() => resetNavigation("/baslangic"));

    it("Onboarding_ListsInvitations_AndAcceptRedirects", async () => {
        let acceptedId: string | undefined;
        server.use(
            http.get(`${API}/auth/session`, () => HttpResponse.json(noCompany)),
            http.get(`${API}/account/invitations`, () => HttpResponse.json([invitation])),
            http.post(`${API}/account/invitations/:id/accept`, ({ params }) => {
                acceptedId = params.id as string;
                return HttpResponse.json(sessionInfo());
            }),
        );
        const { user } = renderOnboarding();

        expect(await screen.findByText("Akdeniz Tekstil")).toBeInTheDocument();
        expect(screen.getByText(/Sahip Kişi/)).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: /katıl/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/panel"));
        expect(acceptedId).toBe("inv-1");
    });

    it("Onboarding_NoInvitations_HidesSection", async () => {
        server.use(
            http.get(`${API}/auth/session`, () => HttpResponse.json(noCompany)),
            http.get(`${API}/account/invitations`, () => HttpResponse.json([])),
        );

        renderOnboarding();

        expect(await screen.findByRole("heading", { name: /firma oluştur/i })).toBeInTheDocument();
        expect(screen.queryByText(/bekleyen davetler/i)).not.toBeInTheDocument();
    });

    it("Onboarding_CreateCompany_ValidatesAndRedirects", async () => {
        let createdName: unknown;
        server.use(
            http.get(`${API}/auth/session`, () => HttpResponse.json(noCompany)),
            http.get(`${API}/account/invitations`, () => HttpResponse.json([])),
            http.post(`${API}/companies`, async ({ request }) => {
                createdName = await request.json();
                return HttpResponse.json({ id: demoCompany.id, name: "Yeni Firma", createdAt: "2026-11-22T00:00:00Z" }, { status: 201 });
            }),
        );
        const { user } = renderOnboarding();
        const name = await screen.findByLabelText(/firma adı/i);

        await user.type(name, "A");
        await user.click(screen.getByRole("button", { name: /^oluştur$/i }));
        expect(await screen.findByText("Firma adı en az 2 karakter olmalıdır.")).toBeInTheDocument();
        expect(createdName).toBeUndefined();

        await user.clear(name);
        await user.type(name, "Yeni Firma");
        await user.click(screen.getByRole("button", { name: /^oluştur$/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/panel"));
        expect(createdName).toEqual({ name: "Yeni Firma" });
    });

    it("Onboarding_WithExistingCompany_ShowsBackLink", async () => {
        server.use(
            http.get(`${API}/auth/session`, () => HttpResponse.json(sessionInfo())),
            http.get(`${API}/account/invitations`, () => HttpResponse.json([])),
        );

        renderOnboarding();

        expect(await screen.findByRole("link", { name: /panele dön/i })).toHaveAttribute("href", "/panel");
    });

    it("Onboarding_GreetsUserAndOffersLogout", async () => {
        server.use(
            http.get(`${API}/auth/session`, () => HttpResponse.json(noCompany)),
            http.get(`${API}/account/invitations`, () => HttpResponse.json([])),
        );

        renderOnboarding();

        expect(await screen.findByText(/hoş geldin, ali veli/i)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: /çıkış yap/i })).toBeInTheDocument();
    });
});
