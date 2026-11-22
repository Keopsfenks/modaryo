import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/server";
import { ApiError, apiFetch } from "./client";

describe("apiFetch", () => {
    it("apiFetch_PostSendsCsrfHeaderAndJson", async () => {
        let seen: Request | undefined;
        let body: unknown;
        server.use(
            http.post("http://localhost:3000/api/identity/auth/login", async ({ request }) => {
                seen = request;
                body = await request.json();
                return HttpResponse.json({ ok: true });
            }),
        );

        const result = await apiFetch<{ ok: boolean }>("/api/identity/auth/login", { method: "POST", body: { email: "a@b.co" } });

        expect(result).toEqual({ ok: true });
        expect(seen?.headers.get("X-Modaryo-Csrf")).toBe("1");
        expect(seen?.headers.get("content-type")).toContain("application/json");
        expect(body).toEqual({ email: "a@b.co" });
    });

    it("apiFetch_GetSendsNoCsrfHeader", async () => {
        let seen: Request | undefined;
        server.use(
            http.get("http://localhost:3000/api/identity/auth/session", ({ request }) => {
                seen = request;
                return HttpResponse.json({});
            }),
        );

        await apiFetch("/api/identity/auth/session");

        expect(seen?.headers.has("X-Modaryo-Csrf")).toBe(false);
    });

    it("apiFetch_204ReturnsUndefined", async () => {
        server.use(http.post("http://localhost:3000/api/identity/auth/logout", () => new HttpResponse(null, { status: 204 })));

        await expect(apiFetch("/api/identity/auth/logout", { method: "POST" })).resolves.toBeUndefined();
    });

    it("apiFetch_202WithoutBodyReturnsUndefined", async () => {
        server.use(http.post("http://localhost:3000/api/identity/auth/register", () => new HttpResponse(null, { status: 202 })));

        await expect(apiFetch("/api/identity/auth/register", { method: "POST", body: {} })).resolves.toBeUndefined();
    });

    it("apiFetch_ThrowsApiErrorWithCodeAndTitle", async () => {
        server.use(
            http.post("http://localhost:3000/api/identity/auth/login", () =>
                HttpResponse.json({ status: 423, title: "Hesabınız 15 dakika kilitli.", code: "account_locked" }, { status: 423 }),
            ),
        );

        const error = await apiFetch("/api/identity/auth/login", { method: "POST", body: {} }).catch((e: unknown) => e);

        expect(error).toBeInstanceOf(ApiError);
        expect((error as ApiError).status).toBe(423);
        expect((error as ApiError).code).toBe("account_locked");
        expect((error as ApiError).message).toBe("Hesabınız 15 dakika kilitli.");
    });

    it("apiFetch_MapsValidationErrorsToFields", async () => {
        server.use(
            http.post("http://localhost:3000/api/identity/account/change-password", () =>
                HttpResponse.json(
                    {
                        status: 400,
                        title: "One or more validation errors occurred.",
                        errors: { Password: ["Şifre en az 10 karakter olmalıdır.", "ikinci"], CurrentPassword: ["x"] },
                    },
                    { status: 400 },
                ),
            ),
        );

        const error = (await apiFetch("/api/identity/account/change-password", { method: "POST", body: {} }).catch((e: unknown) => e)) as ApiError;

        expect(error.fieldErrors).toEqual({ password: "Şifre en az 10 karakter olmalıdır.", currentPassword: "x" });
        expect(error.message).toBe("Formdaki hataları düzeltin.");
    });

    it("apiFetch_NonJsonErrorBody_UsesGenericTitle", async () => {
        server.use(http.get("http://localhost:3000/api/erp/me", () => new HttpResponse("patladı", { status: 500 })));

        const error = (await apiFetch("/api/erp/me").catch((e: unknown) => e)) as ApiError;

        expect(error.status).toBe(500);
        expect(error.message).toBe("Beklenmeyen bir hata oluştu.");
    });

    it("apiFetch_NetworkFailure_ThrowsApiErrorWithStatus0", async () => {
        server.use(http.get("http://localhost:3000/api/erp/me", () => HttpResponse.error()));

        const error = (await apiFetch("/api/erp/me").catch((e: unknown) => e)) as ApiError;

        expect(error).toBeInstanceOf(ApiError);
        expect(error.status).toBe(0);
        expect(error.message).toBe("Sunucuya ulaşılamadı. Bağlantınızı kontrol edin.");
    });
});
