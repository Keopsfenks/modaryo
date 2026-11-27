import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import QuoteEditor from "./QuoteEditor";
import { quoteDto } from "./salesFixtures";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

function setup(permissions: string[]) {
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get(`${IDENTITY}/companies/current/branches`, () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }])),
        http.get(`${IDENTITY}/companies/current/members`, () => HttpResponse.json([])),
        http.get("*/api/erp/current-accounts", () => HttpResponse.json({ items: [], total: 0 })),
        http.get("*/api/erp/catalog/products/*", () => HttpResponse.json({}, { status: 404 })),
        http.post("*/api/erp/invoices/preview", () => HttpResponse.json({}, { status: 400 })),
    );
}

beforeEach(() => resetNavigation("/satis/teklifler/q-1"));

describe("QuoteEditor", () => {
    it("QuoteEditor_EditSentShowsRevisionNotice", async () => {
        setup(["satis.read", "satis.manage"]);
        server.use(http.get("*/api/erp/sales/quotes/q-1", () => HttpResponse.json(quoteDto())));

        renderWithTheme(
            <SessionProvider>
                <QuoteEditor id="q-1" />
            </SessionProvider>,
        );

        expect(await screen.findByText(/yeni revizyon \(Rev\. 2\) açılır/)).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Yeni revizyon olarak kaydet" })).toBeInTheDocument();
        expect(screen.getByRole("button", { name: "Kabul et (siparişe çevir)" })).toBeInTheDocument();
    });

    it("QuoteEditor_OldRevisionIsReadOnly", async () => {
        setup(["satis.read", "satis.manage"]);
        server.use(
            http.get("*/api/erp/sales/quotes/q-1", ({ request }) => {
                const revision = new URL(request.url).searchParams.get("revision");
                return HttpResponse.json(
                    quoteDto({
                        currentRevision: 2,
                        revision: revision ? Number(revision) : 2,
                        revisions: [
                            { revision: 1, date: "2026-11-20", validUntil: null, total: 7000 },
                            { revision: 2, date: "2026-11-22", validUntil: null, total: 7360 },
                        ],
                    }),
                );
            }),
        );

        const { user } = renderWithTheme(
            <SessionProvider>
                <QuoteEditor id="q-1" />
            </SessionProvider>,
        );

        await user.click(await screen.findByText(/^Rev\. 1 ·/));
        expect(await screen.findByText(/Rev\. 1 salt okunurdur/)).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: /Kaydet|revizyon olarak/ })).not.toBeInTheDocument();
    });
});
