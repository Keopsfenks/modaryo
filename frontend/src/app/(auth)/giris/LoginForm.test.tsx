import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import LoginForm from "./LoginForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const LOGIN = "http://localhost:3000/api/identity/auth/login";

async function fillAndSubmit(user: ReturnType<typeof renderWithTheme>["user"], email = "ali@ornek.com", password = "guvenli-sifre-123") {
    await user.type(screen.getByLabelText(/e-posta/i), email);
    await user.type(screen.getByLabelText("Şifre", { selector: "input" }), password);
    await user.click(screen.getByRole("button", { name: /giriş yap/i }));
}

describe("LoginForm", () => {
    beforeEach(() => resetNavigation("/giris"));

    it("Login_Success_RedirectsToNext", async () => {
        resetNavigation("/giris", "next=%2Fhesap");
        server.use(http.post(LOGIN, () => HttpResponse.json(sessionInfo())));
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/hesap"));
    });

    it("Login_WithoutCompany_RedirectsToBaslangic", async () => {
        server.use(http.post(LOGIN, () => HttpResponse.json(sessionInfo({ activeCompany: null, companies: [], permissions: [] }))));
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/baslangic"));
    });

    it("Login_IgnoresExternalNext", async () => {
        resetNavigation("/giris", "next=https%3A%2F%2Fkotu.site");
        server.use(http.post(LOGIN, () => HttpResponse.json(sessionInfo())));
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/panel"));
    });

    it("Login_InvalidCredentials_ShowsServerTitle", async () => {
        server.use(http.post(LOGIN, () => HttpResponse.json(problem(401, "E-posta veya şifre hatalı.", "invalid_credentials"), { status: 401 })));
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);

        expect(await screen.findByText("E-posta veya şifre hatalı.")).toBeInTheDocument();
        expect(router.replace).not.toHaveBeenCalled();
    });

    it("Login_EmailNotConfirmed_OffersResend", async () => {
        let resentFor: unknown;
        server.use(
            http.post(LOGIN, () => HttpResponse.json(problem(403, "E-posta adresinizi henüz doğrulamadınız.", "email_not_confirmed"), { status: 403 })),
            http.post("http://localhost:3000/api/identity/auth/resend-confirmation", async ({ request }) => {
                resentFor = await request.json();
                return new HttpResponse(null, { status: 202 });
            }),
        );
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);
        await user.click(await screen.findByRole("button", { name: /doğrulama e-postasını yeniden gönder/i }));

        expect(await screen.findByText(/doğrulama e-postası gönderildi/i)).toBeInTheDocument();
        expect(resentFor).toEqual({ email: "ali@ornek.com" });
    });

    it("Login_Locked_ShowsMinutes", async () => {
        server.use(
            http.post(LOGIN, () =>
                HttpResponse.json(problem(423, "Çok fazla hatalı deneme yapıldı. Hesabınız 15 dakika kilitli.", "account_locked"), { status: 423 }),
            ),
        );
        const { user } = renderWithTheme(<LoginForm />);

        await fillAndSubmit(user);

        expect(await screen.findByText(/15 dakika kilitli/)).toBeInTheDocument();
    });

    it("Login_EmptyFields_ShowsClientValidationWithoutRequest", async () => {
        let called = false;
        server.use(http.post(LOGIN, () => {
            called = true;
            return HttpResponse.json(sessionInfo());
        }));
        const { user } = renderWithTheme(<LoginForm />);

        await user.click(screen.getByRole("button", { name: /giriş yap/i }));

        expect(await screen.findByText("E-posta adresi gereklidir.")).toBeInTheDocument();
        expect(screen.getByText("Şifre gereklidir.")).toBeInTheDocument();
        expect(called).toBe(false);
    });

    it("Login_RememberMeIsSent", async () => {
        let body: unknown;
        server.use(http.post(LOGIN, async ({ request }) => {
            body = await request.json();
            return HttpResponse.json(sessionInfo());
        }));
        const { user } = renderWithTheme(<LoginForm />);

        await user.click(screen.getByLabelText(/beni hatırla/i));
        await fillAndSubmit(user);

        await waitFor(() => expect(body).toEqual({ email: "ali@ornek.com", password: "guvenli-sifre-123", rememberMe: true }));
    });

    it("Login_HasLinksToForgotPasswordAndRegister", () => {
        renderWithTheme(<LoginForm />);

        expect(screen.getByRole("link", { name: /şifremi unuttum/i })).toHaveAttribute("href", "/sifremi-unuttum");
        expect(screen.getByRole("link", { name: /kayıt ol/i })).toHaveAttribute("href", "/kayit");
    });
});
