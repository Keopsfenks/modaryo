import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { SessionProvider } from "@/session/SessionProvider";
import { sessionInfo } from "@/test/fixtures";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import RatesTable from "./RatesTable";

vi.mock("next/navigation", () => import("@/test/nextNavigation"));

const IDENTITY = "http://localhost:3000/api/identity";

const rate = (currency: string, type: string, value: number, overridden = false) => ({ currency, type, rate: value, effectiveDate: new Date().toISOString().slice(0, 10), source: overridden ? "manual" : "tcmb", overridden });

function setup(permissions: string[]) {
    const puts: unknown[] = [];
    server.use(
        http.get(`${IDENTITY}/auth/session`, () => HttpResponse.json(sessionInfo({ permissions }))),
        http.get("*/api/erp/exchange-rates", () =>
            HttpResponse.json([rate("USD", "forexBuying", 32.5), rate("USD", "forexSelling", 32.6, true), rate("EUR", "forexBuying", 35.1)]),
        ),
        http.put("*/api/erp/exchange-rates/override", async ({ request }) => {
            puts.push(await request.json());
            return HttpResponse.json(rate("USD", "forexBuying", 33, true));
        }),
    );
    return puts;
}

const render = () =>
    renderWithTheme(
        <SessionProvider>
            <RatesTable />
        </SessionProvider>,
    );

describe("RatesTable", () => {
    it("RatesTable_OverrideRequiresPermission", async () => {
        setup(["fatura.read"]);
        render();

        expect(await screen.findByText("32,5000")).toBeInTheDocument();
        expect(screen.getByText("Elle")).toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "USD Döviz Alış düzenle" })).not.toBeInTheDocument();
        expect(screen.queryByRole("button", { name: "Şimdi çek" })).not.toBeInTheDocument();
    });

    it("RatesTable_WithPermission_OverridesRate", async () => {
        const puts = setup(["fatura.read", "kur.manage"]);
        const { user } = render();

        await user.click(await screen.findByRole("button", { name: "USD Döviz Alış düzenle" }));
        const field = await screen.findByLabelText("Kur (TL)");
        await user.clear(field);
        await user.type(field, "33");
        await user.click(screen.getByRole("button", { name: "Kaydet" }));

        expect(await screen.findByText("Kur kaydedildi.")).toBeInTheDocument();
        expect(puts[0]).toMatchObject({ currency: "USD", type: "forexBuying", rate: 33 });
        expect(await screen.findByRole("button", { name: "Şimdi çek" })).toBeInTheDocument();
    });
});
