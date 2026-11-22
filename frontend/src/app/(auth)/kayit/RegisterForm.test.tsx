import { fireEvent, screen, waitFor } from "@testing-library/react";
import { delay, http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import RegisterForm from "./RegisterForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const REGISTER = "http://localhost:3000/api/identity/auth/register";

async function fill(user: ReturnType<typeof renderWithTheme>["user"], password = "guvenli-sifre-123") {
    await user.type(screen.getByLabelText(/ad soyad/i), "Ali Veli");
    await user.type(screen.getByLabelText(/e-posta/i), "ali@ornek.com");
    await user.type(screen.getByLabelText("Şifre", { selector: "input" }), password);
}

describe("RegisterForm", () => {
    beforeEach(() => resetNavigation("/kayit"));

    it("Register_Success_ShowsCheckEmailWithAddress", async () => {
        let body: unknown;
        server.use(http.post(REGISTER, async ({ request }) => {
            body = await request.json();
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<RegisterForm />);

        await fill(user);
        await user.click(screen.getByRole("button", { name: /kayıt ol/i }));

        expect(await screen.findByText(/e-postanızı kontrol edin/i)).toBeInTheDocument();
        expect(screen.getByText(/ali@ornek.com/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: /yeniden gönder/i })).toBeInTheDocument();
        expect(body).toEqual({ fullName: "Ali Veli", email: "ali@ornek.com", password: "guvenli-sifre-123" });
    });

    it("Register_ClientValidation", async () => {
        let called = false;
        server.use(http.post(REGISTER, () => {
            called = true;
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<RegisterForm />);

        await fill(user, "123456789");
        await user.click(screen.getByRole("button", { name: /kayıt ol/i }));

        expect(await screen.findByText("Şifre en az 10 karakter olmalıdır.")).toBeInTheDocument();
        expect(called).toBe(false);
    });

    it("Register_ShowsServerFieldError", async () => {
        server.use(http.post(REGISTER, () =>
            HttpResponse.json(problem(400, "One or more validation errors occurred.", undefined, { Password: ["Bu şifre çok yaygın."] }), { status: 400 })));
        const { user } = renderWithTheme(<RegisterForm />);

        await fill(user);
        await user.click(screen.getByRole("button", { name: /kayıt ol/i }));

        const message = await screen.findByText("Bu şifre çok yaygın.");
        expect(message.id).toBe("password-helper-text");
    });

    it("Register_DisablesSubmitWhileSending", async () => {
        let calls = 0;
        server.use(http.post(REGISTER, async () => {
            calls++;
            await delay(100);
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<RegisterForm />);
        await fill(user);
        const button = screen.getByRole("button", { name: /kayıt ol/i });

        await user.click(button);
        fireEvent.click(button); // ikinci, hızlı tıklama (buton devre dışıyken)

        await screen.findByText(/e-postanızı kontrol edin/i);
        expect(calls).toBe(1);
    });

    it("Register_HasLoginLink", () => {
        renderWithTheme(<RegisterForm />);

        expect(screen.getByRole("link", { name: /giriş yap/i })).toHaveAttribute("href", "/giris");
    });

    it("Register_ResendFromCheckEmailScreen", async () => {
        let resent = false;
        server.use(
            http.post(REGISTER, () => new HttpResponse(null, { status: 202 })),
            http.post("http://localhost:3000/api/identity/auth/resend-confirmation", () => {
                resent = true;
                return new HttpResponse(null, { status: 202 });
            }),
        );
        const { user } = renderWithTheme(<RegisterForm />);
        await fill(user);
        await user.click(screen.getByRole("button", { name: /kayıt ol/i }));

        await user.click(await screen.findByRole("button", { name: /yeniden gönder/i }));

        await waitFor(() => expect(resent).toBe(true));
        expect(await screen.findByText(/yeniden gönderildi/i)).toBeInTheDocument();
    });
});
