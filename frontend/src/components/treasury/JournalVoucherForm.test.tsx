import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetNavigation } from "@/test/nextNavigation";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import JournalVoucherForm from "./JournalVoucherForm";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

type User = ReturnType<typeof renderWithTheme>["user"];

async function fillLine(user: User, index: number, code: string, debit: string, credit: string) {
    await user.type(screen.getByLabelText(`Hesap kodu ${index}`), code);
    if (debit) await user.type(screen.getByLabelText(`Borç ${index}`), debit);
    if (credit) await user.type(screen.getByLabelText(`Alacak ${index}`), credit);
}

function capture(posted: unknown[]) {
    server.use(
        http.post("*/api/erp/treasury/journal-vouchers", async ({ request }) => {
            posted.push(await request.json());
            return HttpResponse.json({ id: "v-1", number: "MH2026000001" }, { status: 201 });
        }),
    );
}

describe("JournalVoucherForm", () => {
    beforeEach(() => resetNavigation("/finans/mahsup"));

    it("JournalVoucherForm_UnbalancedBlocksSave", async () => {
        const posted: unknown[] = [];
        capture(posted);
        const { user } = renderWithTheme(<JournalVoucherForm />);

        await user.type(screen.getByLabelText("Açıklama"), "Düzeltme");
        await fillLine(user, 1, "120", "100", "");
        await fillLine(user, 2, "600", "", "80");

        expect(screen.getByTestId("voucher-diff")).toHaveTextContent("Fark: 20,00");
        expect(screen.getByRole("button", { name: "Kaydet" })).toBeDisabled();
        expect(posted).toHaveLength(0);
    });

    it("JournalVoucherForm_BalancedPostsLines", async () => {
        const posted: unknown[] = [];
        capture(posted);
        const { user } = renderWithTheme(<JournalVoucherForm />);

        await user.type(screen.getByLabelText("Açıklama"), "Düzeltme");
        await fillLine(user, 1, "120", "100", "");
        await fillLine(user, 2, "600", "", "100");

        expect(screen.getByTestId("voucher-diff")).toHaveTextContent("Dengeli");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({
            description: "Düzeltme",
            lines: [
                { accountCode: "120", debit: 100, credit: 0 },
                { accountCode: "600", debit: 0, credit: 100 },
            ],
        });
        expect(await screen.findByText(/MH2026000001/)).toBeInTheDocument();
    });
});
