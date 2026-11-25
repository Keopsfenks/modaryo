import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import TransferForm from "./TransferForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";
const accounts = [
    { id: "a-1", kind: "cash", name: "Ana Kasa", currency: "TRY", branchId: "b-1", iban: null, bankName: null, balance: 100, isActive: true },
    { id: "a-2", kind: "bank", name: "Garanti", currency: "TRY", branchId: "b-1", iban: "TR000", bankName: "Garanti", balance: 0, isActive: true },
];

describe("TransferForm", () => {
    it("TransferForm_SameAccountBlocked", async () => {
        let posts = 0;
        server.use(
            http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions: ["virman.manage"] }))),
            http.get("*/api/erp/treasury/accounts", () => HttpResponse.json(accounts)),
            http.post("*/api/erp/treasury/transfers", () => {
                posts++;
                return HttpResponse.json({ id: "d", number: "VR2026000001", warnings: [] }, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(
            <SessionProvider>
                <TransferForm />
            </SessionProvider>,
        );

        await user.click(await screen.findByRole("combobox", { name: "Çıkış hesabı" }));
        await user.click(await screen.findByRole("option", { name: /Ana Kasa/ }));
        await user.click(screen.getByRole("combobox", { name: "Giriş hesabı" }));
        await user.click(await screen.findByRole("option", { name: /Ana Kasa/ }));
        await user.type(screen.getByRole("textbox", { name: /^Tutar/ }), "10");
        await user.click(screen.getByRole("button", { name: "Virman yap" }));

        expect(await screen.findAllByText("Çıkış ve giriş hesabı aynı olamaz.")).not.toHaveLength(0);
        expect(posts).toBe(0);
    });
});
