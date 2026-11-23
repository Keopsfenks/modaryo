import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CatalogSettingsForm from "./CatalogSettingsForm";

const settings = { codeTemplate: "U{00000}", defaultVatRate: 20, units: ["adet", "kg"], barcodePrefix: "2000000", nextBarcodeSequence: 1, nextCodeSequence: 1 };

describe("CatalogSettingsForm", () => {
    it("CatalogSettings_SavesBarcodePrefixValidation", async () => {
        const puts: unknown[] = [];
        server.use(
            http.get("*/api/erp/settings/katalog", () => HttpResponse.json({ version: 3, value: settings })),
            http.put("*/api/erp/settings/katalog", async ({ request }) => {
                const body = (await request.json()) as { value: unknown };
                puts.push(body);
                return HttpResponse.json({ version: 4, value: body.value });
            }),
        );
        const { user } = renderWithTheme(<CatalogSettingsForm />);

        const prefix = await screen.findByLabelText("Barkod öneki");
        await user.clear(prefix);
        await user.type(prefix, "123");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(screen.getByText("Barkod öneki 7 haneli sayı olmalıdır.")).toBeInTheDocument();
        expect(puts).toHaveLength(0);

        await user.clear(prefix);
        await user.type(prefix, "2100000");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Ayarlar kaydedildi.")).toBeInTheDocument();
        expect(puts).toEqual([{ version: 3, value: { ...settings, barcodePrefix: "2100000" } }]);
    });
});
