import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import GeneralSettingsForm from "./GeneralSettingsForm";

const general = {
    defaultCurrency: "TRY",
    dateFormat: "dd.MM.yyyy",
    weekStart: "monday",
    workDays: ["monday"],
    holidays: [],
    quantityDecimals: 9,
    unitPriceDecimals: 2,
};

describe("GeneralSettingsForm", () => {
    it("GeneralSettings_DecimalsOutOfRange_ShowsErrorAndDoesNotSave", async () => {
        let put = false;
        server.use(
            http.get("*/api/erp/settings/genel", () => HttpResponse.json({ version: 1, value: general })),
            http.put("*/api/erp/settings/genel", () => {
                put = true;
                return HttpResponse.json({ version: 2, value: general });
            }),
        );
        const { user } = renderWithTheme(<GeneralSettingsForm />);

        await user.click(await screen.findByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Miktar ondalık basamağı 0 ile 4 arasında olmalıdır.")).toBeInTheDocument();
        expect(put).toBe(false);
    });
});
