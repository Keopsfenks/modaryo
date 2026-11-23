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
});
