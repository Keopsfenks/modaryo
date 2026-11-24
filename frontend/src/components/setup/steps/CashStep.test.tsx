import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { setupSteps } from "@/config/setupSteps";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CashStep from "./CashStep";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const SESSION = "http://localhost:3000/api/identity/auth/session";
const IBAN = "TR330006100519786457841326";

const branches = () => HttpResponse.json([{ id: "b-1", code: "MRK", name: "Merkez", isActive: true, isDefault: true }]);

describe("CashStep", () => {
    it("WizardCashStep_CreatesKasa", async () => {
        const posts: Record<string, unknown>[] = [];
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.get("*/api/identity/companies/current/branches", branches),
            http.get("*/api/erp/treasury/accounts", () => HttpResponse.json([])),
            http.post("*/api/erp/treasury/accounts", async ({ request }) => {
                posts.push((await request.json()) as Record<string, unknown>);
                return HttpResponse.json({ id: "a-1" }, { status: 201 });
            }),
        );
        const onNext = vi.fn();
        const { user } = renderWithTheme(
            <SessionProvider>
                <CashStep onNext={onNext} onBack={() => undefined} />
            </SessionProvider>,
        );

        const name = await screen.findByRole("textbox", { name: "Kasa adı" });
        expect(name).toHaveValue("Merkez Kasa");
        await user.clear(name);
        await user.type(name, "Ana Kasa");
        await user.click(screen.getByRole("checkbox", { name: "Banka hesabı da ekle" }));
        await user.type(screen.getByRole("textbox", { name: "Banka adı" }), "Garanti");
        await user.type(screen.getByRole("textbox", { name: "IBAN" }), IBAN);
        await user.click(screen.getByRole("button", { name: "Devam" }));

        await waitFor(() => expect(onNext).toHaveBeenCalled());
        expect(posts).toEqual([
            { kind: "cash", name: "Ana Kasa", currency: "TRY", branchId: "b-1" },
            { kind: "bank", name: "Garanti TL", currency: "TRY", branchId: "b-1", iban: IBAN, bankName: "Garanti" },
        ]);
    });

    it("WizardCashStep_ExistingKasaIsNotRecreated", async () => {
        const posts: unknown[] = [];
        server.use(
            http.get(SESSION, () => HttpResponse.json(sessionInfo())),
            http.get("*/api/identity/companies/current/branches", branches),
            http.get("*/api/erp/treasury/accounts", () => HttpResponse.json([{ id: "a-1", kind: "cash", name: "Kasa", currency: "TRY", branchId: "b-1", iban: null, bankName: null, balance: 0, isActive: true }])),
            http.post("*/api/erp/treasury/accounts", async ({ request }) => {
                posts.push(await request.json());
                return HttpResponse.json({ id: "a-2" }, { status: 201 });
            }),
        );
        const onNext = vi.fn();
        const { user } = renderWithTheme(
            <SessionProvider>
                <CashStep onNext={onNext} onBack={() => undefined} />
            </SessionProvider>,
        );

        expect(await screen.findByText("Bir kasanız zaten var; yeniden oluşturulmayacak.")).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "Devam" }));

        await waitFor(() => expect(onNext).toHaveBeenCalled());
        expect(posts).toEqual([]);
    });

    it("WizardCashStep_IsRegisteredAtOrder35", () => {
        const step = setupSteps.find((s) => s.key === "cash");
        expect(step).toMatchObject({ order: 35, title: "İlk kasa" });
    });
});
