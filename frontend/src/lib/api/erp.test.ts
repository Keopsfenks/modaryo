import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { server } from "@/test/server";
import { ApiError, CSRF_HEADER } from "./client";
import { erp } from "./erp";

describe("erp api", () => {
    it("listAccounts_BuildsQueryString", async () => {
        let search = "";
        server.use(
            http.get("*/api/erp/current-accounts", ({ request }) => {
                search = new URL(request.url).search;
                return HttpResponse.json({ items: [], total: 0 });
            }),
        );

        await erp.listAccounts({ role: "supplier", page: 2, pageSize: 25 });

        expect(search).toBe("?role=supplier&page=2&pageSize=25");
    });

    it("listAccounts_OmitsUndefinedParams", async () => {
        let search = "x";
        server.use(
            http.get("*/api/erp/current-accounts", ({ request }) => {
                search = new URL(request.url).search;
                return HttpResponse.json({ items: [], total: 0 });
            }),
        );

        await erp.listAccounts({ search: undefined, includeInactive: true });

        expect(search).toBe("?includeInactive=true");
    });

    it("setActive_PostsToActivateAndDeactivate", async () => {
        const paths: string[] = [];
        server.use(
            http.post("*/api/erp/current-accounts/:id/:action", ({ request }) => {
                paths.push(new URL(request.url).pathname);
                expect(request.headers.get(CSRF_HEADER)).toBe("1");
                return new HttpResponse(null, { status: 204 });
            }),
        );

        await erp.setActive("a1", true);
        await erp.setActive("a1", false);

        expect(paths).toEqual(["/api/erp/current-accounts/a1/activate", "/api/erp/current-accounts/a1/deactivate"]);
    });

    it("cancelEntry_SendsReason", async () => {
        let body: unknown;
        server.use(
            http.post("*/api/erp/current-accounts/a1/entries/e1/cancel", async ({ request }) => {
                body = await request.json();
                return new HttpResponse(null, { status: 204 });
            }),
        );

        await erp.cancelEntry("a1", "e1", "Hatalı giriş");

        expect(body).toEqual({ reason: "Hatalı giriş" });
    });

    it("statement_PassesDateRange", async () => {
        let search = "";
        server.use(
            http.get("*/api/erp/current-accounts/a1/statement", ({ request }) => {
                search = new URL(request.url).search;
                return HttpResponse.json({});
            }),
        );

        await erp.statement("a1", "2026-01-01", "2026-03-31");

        expect(search).toBe("?from=2026-01-01&to=2026-03-31");
    });

    it("errors_ExposeProblemCode", async () => {
        server.use(
            http.delete("*/api/erp/current-accounts/a1", () =>
                HttpResponse.json({ status: 409, title: "Hareketli kart silinemez.", code: "has_movements" }, { status: 409 }),
            ),
        );

        const error = await erp.deleteAccount("a1").catch((e: unknown) => e);

        expect(error).toBeInstanceOf(ApiError);
        expect((error as ApiError).code).toBe("has_movements");
    });
});
