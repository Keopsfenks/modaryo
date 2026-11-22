import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ForgotPasswordForm from "./ForgotPasswordForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const FORGOT = "http://localhost:3000/api/identity/auth/forgot-password";

describe("ForgotPasswordForm", () => {
    beforeEach(() => resetNavigation("/sifremi-unuttum"));

    it("Forgot_AlwaysShowsSameConfirmation", async () => {
        let body: unknown;
        server.use(http.post(FORGOT, async ({ request }) => {
            body = await request.json();
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<ForgotPasswordForm />);

        await user.type(screen.getByLabelText(/e-posta/i), "ali@ornek.com");
        await user.click(screen.getByRole("button", { name: /sıfırlama bağlantısı gönder/i }));

        expect(await screen.findByText(/kayıtlı bir hesap varsa sıfırlama bağlantısı gönderdik/i)).toBeInTheDocument();
        expect(body).toEqual({ email: "ali@ornek.com" });
    });

    it("Forgot_RateLimited_ShowsServerTitle", async () => {
        server.use(http.post(FORGOT, () =>
            HttpResponse.json(problem(429, "Çok fazla istek yaptınız. Lütfen daha sonra tekrar deneyin.", "rate_limited"), { status: 429 })));
        const { user } = renderWithTheme(<ForgotPasswordForm />);

        await user.type(screen.getByLabelText(/e-posta/i), "ali@ornek.com");
        await user.click(screen.getByRole("button", { name: /sıfırlama bağlantısı gönder/i }));

        expect(await screen.findByText(/çok fazla istek yaptınız/i)).toBeInTheDocument();
    });

    it("Forgot_InvalidEmail_NoRequest", async () => {
        const { user } = renderWithTheme(<ForgotPasswordForm />);

        await user.type(screen.getByLabelText(/e-posta/i), "gecersiz");
        await user.click(screen.getByRole("button", { name: /sıfırlama bağlantısı gönder/i }));

        expect(await screen.findByText("Geçerli bir e-posta adresi girin.")).toBeInTheDocument();
    });
});
