import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import DocumentSettingsForm from "./DocumentSettingsForm";

const value = {
    accentColor: "#5D87FF",
    footerNote: null,
    showIbans: true,
    invoiceEmailSubject: "{firma} - {belgeNo} numaralı fatura",
    invoiceEmailBody: "Sayın {alici}, {belgeNo} numaralı faturanız ektedir.",
};

describe("DocumentSettingsForm", () => {
    it("DocumentSettings_PreviewRefreshesOnSave", async () => {
        let previews = 0;
        let put: { version: number; value: { accentColor: string } } | undefined;
        server.use(
            http.get("*/api/erp/settings/belgeler", () => HttpResponse.json({ version: 3, value })),
            http.put("*/api/erp/settings/belgeler", async ({ request }) => {
                put = (await request.json()) as typeof put;
                return HttpResponse.json({ version: 4, value: put!.value });
            }),
            http.get("*/api/erp/invoices/sample-pdf", () => {
                previews++;
                return new HttpResponse(new Uint8Array([37, 80, 68, 70]), { headers: { "Content-Type": "application/pdf" } });
            }),
        );
        const { user } = renderWithTheme(<DocumentSettingsForm />);

        const color = await screen.findByLabelText("Vurgu rengi");
        await waitFor(() => expect(previews).toBe(1));
        expect(screen.getByText("E-posta şablon değişkenleri")).toBeInTheDocument();

        await user.clear(color);
        await user.type(color, "#112233");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Ayarlar kaydedildi.")).toBeInTheDocument();
        expect(put).toMatchObject({ version: 3, value: { accentColor: "#112233" } });
        await waitFor(() => expect(previews).toBe(2));
    });

    it("DocumentSettings_InvalidColor_BlocksSave", async () => {
        let puts = 0;
        server.use(
            http.get("*/api/erp/settings/belgeler", () => HttpResponse.json({ version: 3, value })),
            http.put("*/api/erp/settings/belgeler", () => {
                puts++;
                return HttpResponse.json({ version: 4, value });
            }),
            http.get("*/api/erp/invoices/sample-pdf", () => new HttpResponse(new Uint8Array([37]), { headers: { "Content-Type": "application/pdf" } })),
        );
        const { user } = renderWithTheme(<DocumentSettingsForm />);

        const color = await screen.findByLabelText("Vurgu rengi");
        await user.clear(color);
        await user.type(color, "mavi");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(screen.getByText("#RRGGBB biçiminde olmalıdır.")).toBeInTheDocument();
        expect(puts).toBe(0);
    });
});
