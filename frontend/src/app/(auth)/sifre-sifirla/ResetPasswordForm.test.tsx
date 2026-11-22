import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ResetPasswordForm from "./ResetPasswordForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const RESET = "http://localhost:3000/api/identity/auth/reset-password";

async function fill(user: ReturnType<typeof renderWithTheme>["user"], password: string, repeat: string) {
    await user.type(screen.getByLabelText("Yeni şifre", { selector: "input" }), password);
    await user.type(screen.getByLabelText("Yeni şifre (tekrar)", { selector: "input" }), repeat);
    await user.click(screen.getByRole("button", { name: /şifreyi değiştir/i }));
}

describe("ResetPasswordForm", () => {
    beforeEach(() => resetNavigation("/sifre-sifirla", "e=ali%2Btest%40ornek.com&t=tok%3Den"));

    it("Reset_MismatchedPasswords_NoRequest", async () => {
        let called = false;
        server.use(http.post(RESET, () => {
            called = true;
            return new HttpResponse(null, { status: 204 });
        }));
        const { user } = renderWithTheme(<ResetPasswordForm />);

        await fill(user, "yepyeni-sifre-789", "baska-sifre-789");

        expect(await screen.findByText("Şifreler eşleşmiyor.")).toBeInTheDocument();
        expect(called).toBe(false);
    });

    it("Reset_Success_ShowsLoginButton", async () => {
        let body: unknown;
        server.use(http.post(RESET, async ({ request }) => {
            body = await request.json();
            return new HttpResponse(null, { status: 204 });
        }));
        const { user } = renderWithTheme(<ResetPasswordForm />);

        await fill(user, "yepyeni-sifre-789", "yepyeni-sifre-789");

        expect(await screen.findByText(/şifreniz değiştirildi/i)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /giriş yap/i })).toHaveAttribute("href", "/giris");
        expect(body).toEqual({ email: "ali+test@ornek.com", token: "tok=en", newPassword: "yepyeni-sifre-789" });
    });

    it("Reset_InvalidToken_OffersNewLink", async () => {
        server.use(http.post(RESET, () =>
            HttpResponse.json(problem(400, "Şifre sıfırlama bağlantısı geçersiz veya süresi dolmuş.", "invalid_token"), { status: 400 })));
        const { user } = renderWithTheme(<ResetPasswordForm />);

        await fill(user, "yepyeni-sifre-789", "yepyeni-sifre-789");

        expect(await screen.findByText(/geçersiz veya süresi dolmuş/i)).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /yeni bağlantı iste/i })).toHaveAttribute("href", "/sifremi-unuttum");
    });

    it("Reset_MissingParams_ShowsInvalidLink", () => {
        resetNavigation("/sifre-sifirla", "");

        renderWithTheme(<ResetPasswordForm />);

        expect(screen.getByText(/bağlantı geçersiz/i)).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: /şifreyi değiştir/i })).not.toBeInTheDocument();
    });
});
