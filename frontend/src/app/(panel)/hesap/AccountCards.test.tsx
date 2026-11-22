import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { problem, sessionInfo } from "@/test/fixtures";
import { resetNavigation, router } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ChangePasswordCard from "./ChangePasswordCard";
import ProfileCard from "./ProfileCard";
import SessionsCard from "./SessionsCard";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const API = "http://localhost:3000/api/identity";
const chromeUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
const sessions = [
    { id: "aaaaaaaaaaaaaaaa", createdAt: "2026-11-22T08:00:00Z", ip: "203.0.113.5", userAgent: chromeUa, isCurrent: true, rememberMe: false },
    { id: "bbbbbbbbbbbbbbbb", createdAt: "2026-11-21T08:00:00Z", ip: "198.51.100.7", userAgent: "curl/8.23.0", isCurrent: false, rememberMe: true },
];

function withSession(ui: React.ReactElement) {
    return renderWithTheme(<SessionProvider>{ui}</SessionProvider>);
}

describe("Hesabım kartları", () => {
    beforeEach(() => {
        resetNavigation("/hesap");
        server.use(http.get(`${API}/auth/session`, () => HttpResponse.json(sessionInfo())));
    });

    it("Profile_SavesAndRefreshesSession", async () => {
        let saved: unknown;
        let sessionCalls = 0;
        server.use(
            http.get(`${API}/auth/session`, () => {
                sessionCalls++;
                return HttpResponse.json(sessionInfo());
            }),
            http.patch(`${API}/account/profile`, async ({ request }) => {
                saved = await request.json();
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = withSession(<ProfileCard />);
        const name = await screen.findByLabelText(/ad soyad/i);
        expect(screen.getByLabelText(/e-posta/i)).toHaveAttribute("readonly");

        await user.clear(name);
        await user.type(name, "Ali Can Veli");
        await user.click(screen.getByRole("button", { name: /kaydet/i }));

        expect(await screen.findByText(/bilgileriniz kaydedildi/i)).toBeInTheDocument();
        expect(saved).toEqual({ fullName: "Ali Can Veli" });
        await waitFor(() => expect(sessionCalls).toBe(2));
    });

    it("ChangePassword_WrongCurrent_ShowsFieldError", async () => {
        server.use(http.post(`${API}/account/change-password`, () =>
            HttpResponse.json(problem(400, "Validation", undefined, { CurrentPassword: ["Mevcut şifre hatalı."] }), { status: 400 })));
        const { user } = withSession(<ChangePasswordCard />);

        await user.type(await screen.findByLabelText("Mevcut şifre", { selector: "input" }), "yanlis-sifre-000");
        await user.type(screen.getByLabelText("Yeni şifre", { selector: "input" }), "yepyeni-sifre-789");
        await user.type(screen.getByLabelText("Yeni şifre (tekrar)", { selector: "input" }), "yepyeni-sifre-789");
        await user.click(screen.getByRole("button", { name: /şifreyi değiştir/i }));

        const message = await screen.findByText("Mevcut şifre hatalı.");
        expect(message.id).toBe("currentPassword-helper-text");
    });

    it("ChangePassword_Success_ClearsFormAndShowsMessage", async () => {
        server.use(http.post(`${API}/account/change-password`, () => new HttpResponse(null, { status: 204 })));
        const { user } = withSession(<ChangePasswordCard />);

        await user.type(await screen.findByLabelText("Mevcut şifre", { selector: "input" }), "guvenli-sifre-123");
        await user.type(screen.getByLabelText("Yeni şifre", { selector: "input" }), "yepyeni-sifre-789");
        await user.type(screen.getByLabelText("Yeni şifre (tekrar)", { selector: "input" }), "yepyeni-sifre-789");
        await user.click(screen.getByRole("button", { name: /şifreyi değiştir/i }));

        expect(await screen.findByText(/diğer cihazlardaki oturumlarınız kapatıldı/i)).toBeInTheDocument();
        expect(screen.getByLabelText("Mevcut şifre", { selector: "input" })).toHaveValue("");
    });

    it("Sessions_CurrentRowHasNoRevokeButton", async () => {
        server.use(http.get(`${API}/account/sessions`, () => HttpResponse.json(sessions)));

        withSession(<SessionsCard />);

        const current = (await screen.findByText("Chrome / Windows")).closest("tr")!;
        expect(within(current).getByText(/bu cihaz/i)).toBeInTheDocument();
        expect(within(current).queryByRole("button", { name: /kapat/i })).not.toBeInTheDocument();
        const other = screen.getByText("Bilinmeyen cihaz").closest("tr")!;
        expect(within(other).getByText("198.51.100.7")).toBeInTheDocument();
        expect(within(other).getByRole("button", { name: /kapat/i })).toBeInTheDocument();
    });

    it("Sessions_Revoke_RemovesRow", async () => {
        let revoked: string | undefined;
        server.use(
            http.get(`${API}/account/sessions`, () => HttpResponse.json(sessions)),
            http.delete(`${API}/account/sessions/:id`, ({ params }) => {
                revoked = params.id as string;
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = withSession(<SessionsCard />);
        const other = (await screen.findByText("Bilinmeyen cihaz")).closest("tr")!;

        await user.click(within(other).getByRole("button", { name: /kapat/i }));

        await waitFor(() => expect(screen.queryByText("Bilinmeyen cihaz")).not.toBeInTheDocument());
        expect(revoked).toBe("bbbbbbbbbbbbbbbb");
    });

    it("Sessions_LogoutAll_ConfirmsThenRedirects", async () => {
        let loggedOutAll = false;
        server.use(
            http.get(`${API}/account/sessions`, () => HttpResponse.json(sessions)),
            http.post(`${API}/auth/logout-all`, () => {
                loggedOutAll = true;
                return new HttpResponse(null, { status: 204 });
            }),
        );
        const { user } = withSession(<SessionsCard />);

        await user.click(await screen.findByRole("button", { name: /tüm cihazlardan çıkış yap/i }));
        const dialog = await screen.findByRole("dialog");
        expect(loggedOutAll).toBe(false);
        await user.click(within(dialog).getByRole("button", { name: /çıkış yap/i }));

        await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/giris"));
        expect(loggedOutAll).toBe(true);
    });
});
