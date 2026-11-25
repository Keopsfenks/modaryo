import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import ExpenseForm, { splitVat } from "./ExpenseForm";

const categories = [
    { id: "k-1", code: "kira", name: "Kira", accountCode: "770", isIncome: false, isSystem: true, isActive: true },
    { id: "k-2", code: "faiz", name: "Faiz geliri", accountCode: "642", isIncome: true, isSystem: true, isActive: true },
];
const accounts = [{ id: "a-1", kind: "cash", name: "Ana Kasa", currency: "TRY", branchId: "b-1", iban: null, bankName: null, balance: 100, isActive: true }];

describe("ExpenseForm", () => {
    it("ExpenseForm_VatSplitPreview", async () => {
        const posted: unknown[] = [];
        server.use(
            http.get("*/api/erp/treasury/expense-categories", () => HttpResponse.json(categories)),
            http.get("*/api/erp/treasury/accounts", () => HttpResponse.json(accounts)),
            http.post("*/api/erp/treasury/expenses", async ({ request }) => {
                posted.push(await request.json());
                return HttpResponse.json({ id: "d", number: "MF2026000001", netAmount: 1000, vatAmount: 200, warnings: [] }, { status: 201 });
            }),
        );
        const { user } = renderWithTheme(<ExpenseForm />);

        await user.type(screen.getByRole("textbox", { name: /^Tutar/ }), "1200");
        expect(await screen.findByTestId("vat-preview")).toHaveTextContent("Net: 1.000,00 ₺ · KDV: 200,00 ₺ · Toplam: 1.200,00 ₺");

        await user.click(await screen.findByRole("combobox", { name: "Kategori" }));
        await user.click(await screen.findByRole("option", { name: "Kira" }));
        await user.click(screen.getByRole("combobox", { name: "Hesap" }));
        await user.click(await screen.findByRole("option", { name: /Ana Kasa/ }));
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        await waitFor(() => expect(posted).toHaveLength(1));
        expect(posted[0]).toMatchObject({ kind: "expense", categoryId: "k-1", amount: 1200, vatRate: 20, accountId: "a-1", currentAccountId: null });
        expect(await screen.findByText(/MF2026000001/)).toBeInTheDocument();
    });

    it("splitVat_MatchesServerRounding", () => {
        expect(splitVat(100, 18)).toEqual({ net: 84.75, vat: 15.25 });
        expect(splitVat(100, 0)).toEqual({ net: 100, vat: 0 });
    });
});
