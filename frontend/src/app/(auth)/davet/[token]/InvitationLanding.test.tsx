import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import InvitationLanding from "./InvitationLanding";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const API = "http://localhost:3000/api/identity";
const TOKEN = "abc_DEF-123";
const preview = (accountExists: boolean) => ({
    companyName: "Akdeniz Tekstil",
    invitedBy: "Sahip Kişi",
    email: "veli@ornek.com",
    expiresAt: "2026-11-28T00:00:00Z",
    accountExists,
});
const signedOut = () => http.get(`${API}/auth/session`, () => HttpResponse.json(problem(401, "Unauthorized"), { status: 401 }));
const signedInAs = (email: string) => http.get(`${API}/auth/session`, () =>
    HttpResponse.json(sessionInfo({ user: { id: "u1", email, fullName: "Veli" }, activeCompany: null, companies: [], permissions: [] })));

describe("InvitationLanding", () => {
    beforeEach(() => resetNavigation(`/davet/${TOKEN}`));

    it("Invitation_NotFound_ShowsMessage", async () => {
        server.use(http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(problem(404, "Not found"), { status: 404 })), signedOut());

        renderWithTheme(<InvitationLanding token={TOKEN} />);

        expect(await screen.findByText(/geçersiz, kullanılmış veya süresi dolmuş/i)).toBeInTheDocument();
    });

    it("Invitation_SignedInMatchingEmail_Joins", async () => {
        let accepted = false;
        server.use(
            http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(preview(true))),
            signedInAs("Veli@Ornek.com"),
            http.post(`${API}/invitations/${TOKEN}/accept`, () => {
                accepted = true;
                return HttpResponse.json(sessionInfo());
            }),
        );
        const { user } = renderWithTheme(<InvitationLanding token={TOKEN} />);

        await user.click(await screen.findByRole("button", { name: /akdeniz tekstil firmasına katıl/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/panel"));
        expect(accepted).toBe(true);
    });

    it("Invitation_SignedInOtherEmail_OffersSwitchAccount", async () => {
        let loggedOut = false;
        server.use(
            http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(preview(true))),
            signedInAs("baska@ornek.com"),
            http.post(`${API}/auth/logout`, () => {
                loggedOut = true;
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = renderWithTheme(<InvitationLanding token={TOKEN} />);

        expect(await screen.findByText(/veli@ornek.com adresine gönderildi/i)).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: /çıkış yap ve o hesapla gir/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith(`/giris?next=%2Fdavet%2F${TOKEN}`));
        expect(loggedOut).toBe(true);
    });

    it("Invitation_SignedOutExistingAccount_LinksToLogin", async () => {
        server.use(http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(preview(true))), signedOut());

        renderWithTheme(<InvitationLanding token={TOKEN} />);

        expect(await screen.findByRole("link", { name: /giriş yap/i })).toHaveAttribute("href", `/giris?next=%2Fdavet%2F${TOKEN}`);
        expect(screen.queryByLabelText(/ad soyad/i)).not.toBeInTheDocument();
    });

    it("Invitation_Register_SendsInviteTokenAndLocksEmail", async () => {
        let body: unknown;
        server.use(
            http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(preview(false))),
            signedOut(),
            http.post(`${API}/auth/register`, async ({ request }) => {
                body = await request.json();
                return HttpResponse.json(sessionInfo());
            }),
        );
        const { user } = renderWithTheme(<InvitationLanding token={TOKEN} />);

        const email = await screen.findByLabelText(/e-posta/i);
        expect(email).toHaveValue("veli@ornek.com");
        expect(email).toHaveAttribute("readonly");
        await user.type(screen.getByLabelText(/ad soyad/i), "Veli Kaya");
        await user.type(screen.getByLabelText("Şifre", { selector: "input" }), "guvenli-sifre-123");
        await user.click(screen.getByRole("button", { name: /hesabı oluştur ve katıl/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/panel"));
        expect(body).toEqual({ email: "veli@ornek.com", fullName: "Veli Kaya", password: "guvenli-sifre-123", inviteToken: TOKEN });
    });

    it("Invitation_Register_AccountExists_ShowsLogin", async () => {
        server.use(
            http.get(`${API}/invitations/${TOKEN}`, () => HttpResponse.json(preview(false))),
            signedOut(),
            http.post(`${API}/auth/register`, () =>
                HttpResponse.json(problem(409, "Bu e-postayla bir hesap var. Giriş yapıp daveti kabul edin.", "account_exists"), { status: 409 })),
        );
        const { user } = renderWithTheme(<InvitationLanding token={TOKEN} />);

        await user.type(await screen.findByLabelText(/ad soyad/i), "Veli Kaya");
        await user.type(screen.getByLabelText("Şifre", { selector: "input" }), "guvenli-sifre-123");
        await user.click(screen.getByRole("button", { name: /hesabı oluştur ve katıl/i }));

        expect(await screen.findByText(/bu e-postayla bir hesap var/i)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /giriş yap/i })).toHaveAttribute("href", `/giris?next=%2Fdavet%2F${TOKEN}`);
    });
});
