import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import FinanceSettingsForm from "./FinanceSettingsForm";

describe("FinanceSettingsForm", () => {
    it("Finance_ClearingLock_AsksConfirmation", async () => {
        let put: unknown;
        server.use(
            http.get("*/api/erp/settings/finans", () => HttpResponse.json({ version: 2, value: { periodLockDate: "2026-01-31" } })),
            http.put("*/api/erp/settings/finans", async ({ request }) => {
                put = await request.json();
                return HttpResponse.json({ version: 3, value: { periodLockDate: null } });
            }),
        );
        const { user } = renderWithTheme(<FinanceSettingsForm />);

        const date = await screen.findByLabelText("Dönem kilit tarihi");
        expect(screen.getByText("Bu tarih ve öncesine kayıt, iptal ve maliyet yeniden hesabı yapılamaz.")).toBeInTheDocument();
        await user.clear(date);
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Dönem kilidi kaldırılsın mı?")).toBeInTheDocument();
        expect(put).toBeUndefined();

        await user.click(screen.getByRole("button", { name: "Kilidi kaldır" }));

        expect(await screen.findByText("Ayarlar kaydedildi.")).toBeInTheDocument();
        expect(put).toEqual({ version: 2, value: { periodLockDate: null } });
    });

    it("Finance_MappingEdit_SendsMappingAndKeepsOtherFields", async () => {
        let put: { value: Record<string, unknown> } | undefined;
        const initial = { periodLockDate: null, rateType: "forexBuying", expenseCategoryCodes: { kira: "770" } };
        server.use(
            http.get("*/api/erp/settings/finans", () => HttpResponse.json({ version: 4, value: initial })),
            http.put("*/api/erp/settings/finans", async ({ request }) => {
                put = (await request.json()) as { value: Record<string, unknown> };
                return HttpResponse.json({ version: 5, value: put.value });
            }),
        );
        const { user } = renderWithTheme(<FinanceSettingsForm />);

        await user.click(await screen.findByRole("tab", { name: "Hesap eşlemesi" }));
        const sales = screen.getByLabelText("Satışlar");
        expect(sales).toHaveValue("600");
        await user.clear(sales);
        await user.type(sales, "601");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Ayarlar kaydedildi.")).toBeInTheDocument();
        expect(put?.value).toMatchObject({ rateType: "forexBuying", expenseCategoryCodes: { kira: "770" }, accountMapping: { sales: "601", customers: "120" }, subAccountPerCari: false });
    });

    it("Finance_MappingInvalidCode_BlocksSave", async () => {
        let called = false;
        server.use(
            http.get("*/api/erp/settings/finans", () => HttpResponse.json({ version: 4, value: { periodLockDate: null } })),
            http.put("*/api/erp/settings/finans", () => ((called = true), HttpResponse.json({ version: 5, value: {} }))),
        );
        const { user } = renderWithTheme(<FinanceSettingsForm />);

        await user.click(await screen.findByRole("tab", { name: "Hesap eşlemesi" }));
        const cash = screen.getByLabelText("Kasa");
        await user.clear(cash);
        await user.type(cash, "abc");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText(/Hesap kodları rakam, nokta ve tire/)).toBeInTheDocument();
        expect(called).toBe(false);
    });
});
