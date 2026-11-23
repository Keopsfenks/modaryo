import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { problem } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ContactForm from "./ContactForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const CONTACT = "http://localhost:3000/api/identity/public/contact";

async function fillRequired(user: ReturnType<typeof renderWithTheme>["user"], consent = true) {
    await user.type(screen.getByLabelText(/ad soyad/i), "Ayşe Yılmaz");
    await user.type(screen.getByLabelText(/e-posta/i), "ayse@ornek.com");
    if (consent) await user.click(screen.getByRole("checkbox", { name: /kvkk/i }));
}

describe("ContactForm", () => {
    beforeEach(() => resetNavigation("/demo"));

    it("ContactForm_RequiresConsent", async () => {
        let called = false;
        server.use(http.post(CONTACT, () => {
            called = true;
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<ContactForm topic="demo" />);

        await fillRequired(user, false);
        await user.click(screen.getByRole("button", { name: /gönder/i }));

        expect(await screen.findByText(/kvkk aydınlatma metnini onaylamalısınız/i)).toBeInTheDocument();
        expect(called).toBe(false);
    });

    it("ContactForm_PrefillsInterestFromQuery", async () => {
        resetNavigation("/demo", "ilgi=satis");
        let body: Record<string, unknown> = {};
        server.use(http.post(CONTACT, async ({ request }) => {
            body = (await request.json()) as Record<string, unknown>;
            return new HttpResponse(null, { status: 202 });
        }));
        const { user } = renderWithTheme(<ContactForm topic="demo" />);

        expect(screen.getByLabelText(/İlgilendiğiniz modül/)).toHaveValue("satis");
        await fillRequired(user);
        await user.click(screen.getByRole("button", { name: /gönder/i }));

        await screen.findByText(/teşekkürler/i);
        expect(body).toMatchObject({ interest: "satis", topic: "demo", kvkkConsent: true, website: "", name: "Ayşe Yılmaz", email: "ayse@ornek.com" });
    });

    it("ContactForm_Success_ShowsThanks", async () => {
        server.use(http.post(CONTACT, () => new HttpResponse(null, { status: 202 })));
        const { user } = renderWithTheme(<ContactForm topic="iletisim" />);

        await fillRequired(user);
        await user.click(screen.getByRole("button", { name: /gönder/i }));

        expect(await screen.findByText("Teşekkürler, en kısa sürede dönüş yapacağız.")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: /gönder/i })).not.toBeInTheDocument();
        expect(screen.queryByLabelText(/ad soyad/i)).not.toBeInTheDocument();
    });

    it("ContactForm_429_ShowsRateLimitMessage", async () => {
        server.use(http.post(CONTACT, () => HttpResponse.json(problem(429, "x", "rate_limited"), { status: 429 })));
        const { user } = renderWithTheme(<ContactForm topic="demo" />);

        await fillRequired(user);
        await user.click(screen.getByRole("button", { name: /gönder/i }));

        expect(await screen.findByText("Çok fazla deneme yaptınız, lütfen biraz sonra tekrar deneyin.")).toBeInTheDocument();
        expect(screen.getByRole("button", { name: /gönder/i })).toBeEnabled();
    });

    it("ContactForm_HoneypotNotInAccessibilityTree", () => {
        const { container } = renderWithTheme(<ContactForm topic="demo" />);

        expect(screen.queryByRole("textbox", { name: /web sitesi/i })).not.toBeInTheDocument();
        const honeypot = container.querySelector<HTMLInputElement>('input[name="website"]')!;
        expect(honeypot).toBeInTheDocument();
        expect(honeypot.closest("[aria-hidden='true']")).not.toBeNull();
        expect(honeypot.tabIndex).toBe(-1);
        expect(honeypot.autocomplete).toBe("off");
        expect(getComputedStyle(honeypot.closest("[aria-hidden='true']")!).display).not.toBe("none");
    });

    it("ContactForm_Honeypot_SentEmptyByHumans_AndFilledValuePassedThrough", async () => {
        let body: Record<string, unknown> = {};
        server.use(http.post(CONTACT, async ({ request }) => {
            body = (await request.json()) as Record<string, unknown>;
            return new HttpResponse(null, { status: 202 });
        }));
        const { user, container } = renderWithTheme(<ContactForm topic="demo" />);

        await fillRequired(user);
        await user.type(container.querySelector('input[name="website"]')!, "http://spam");
        await user.click(screen.getByRole("button", { name: /gönder/i }));

        await screen.findByText(/teşekkürler/i);
        expect(body.website).toBe("http://spam");
    });

    it("ContactForm_KvkkLinksToPolicy", () => {
        renderWithTheme(<ContactForm topic="demo" />);

        expect(screen.getByRole("link", { name: /kvkk aydınlatma metni/i })).toHaveAttribute("href", "/kvkk");
    });
});
