import { StrictMode } from "react";
import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ConfirmEmail from "./ConfirmEmail";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const CONFIRM = "http://localhost:3000/api/identity/auth/confirm-email";
const USER_ID = "0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90";

describe("ConfirmEmail", () => {
    beforeEach(() => resetNavigation("/eposta-dogrula", `u=${USER_ID}&t=a%2Bb%2Fc%3D`));

    it("ConfirmEmail_Success_RedirectsToBaslangic", async () => {
        let body: unknown;
        server.use(http.post(CONFIRM, async ({ request }) => {
            body = await request.json();
            return HttpResponse.json(sessionInfo({ activeCompany: null, companies: [], permissions: [] }));
        }));

        renderWithTheme(<ConfirmEmail />);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/baslangic"));
        expect(body).toEqual({ userId: USER_ID, token: "a+b/c=" });
    });

    it("ConfirmEmail_CallsApiOnlyOnce", async () => {
        let calls = 0;
        server.use(http.post(CONFIRM, () => {
            calls++;
            return HttpResponse.json(sessionInfo());
        }));

        renderWithTheme(<StrictMode><ConfirmEmail /></StrictMode>);

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/"));
        expect(calls).toBe(1);
    });

    it("ConfirmEmail_InvalidToken_ShowsResendForm", async () => {
        let resentFor: unknown;
        server.use(
            http.post(CONFIRM, () => HttpResponse.json(problem(400, "Doğrulama bağlantısı geçersiz veya süresi dolmuş.", "invalid_token"), { status: 400 })),
            http.post("http://localhost:3000/api/identity/auth/resend-confirmation", async ({ request }) => {
                resentFor = await request.json();
                return new HttpResponse(null, { status: 202 });
            }),
        );
        const { user } = renderWithTheme(<ConfirmEmail />);

        expect(await screen.findByText(/geçersiz veya süresi dolmuş/i)).toBeInTheDocument();
        await user.type(screen.getByLabelText(/e-posta/i), "ali@ornek.com");
        await user.click(screen.getByRole("button", { name: /yeni bağlantı gönder/i }));

        expect(await screen.findByText(/yeni bir doğrulama bağlantısı gönderdik/i)).toBeInTheDocument();
        expect(resentFor).toEqual({ email: "ali@ornek.com" });
    });

    it("ConfirmEmail_MissingParams_ShowsErrorWithoutRequest", async () => {
        resetNavigation("/eposta-dogrula", "");
        let called = false;
        server.use(http.post(CONFIRM, () => {
            called = true;
            return HttpResponse.json(sessionInfo());
        }));

        renderWithTheme(<ConfirmEmail />);

        expect(await screen.findByText(/geçersiz veya süresi dolmuş/i)).toBeInTheDocument();
        expect(called).toBe(false);
    });
});
