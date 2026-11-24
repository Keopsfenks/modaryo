import { useState } from "react";
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import { server } from "@/test/server";
import CurrencyRateField, { type RateState } from "./CurrencyRateField";

function Harness() {
    const [rate, setRate] = useState<RateState>({ value: "", overridden: false });
    return <CurrencyRateField currency="USD" date="2026-03-10" value={rate} onChange={setRate} />;
}

describe("CurrencyRateField", () => {
    it("CurrencyRateField_FetchesAndMarksOverride", async () => {
        let url = "";
        server.use(
            http.get("*/api/erp/exchange-rates/lookup", ({ request }) => {
                url = request.url;
                return HttpResponse.json({ currency: "USD", type: "forexBuying", rate: 32.5, effectiveDate: "2026-03-10", source: "tcmb", overridden: false });
            }),
        );
        const { user } = renderWithTheme(<Harness />);

        const field = await screen.findByLabelText("Kur (USD/TL)");
        await screen.findByDisplayValue("32,5");
        expect(url).toContain("currency=USD");
        expect(url).toContain("date=2026-03-10");
        expect(screen.getByText("TCMB")).toBeInTheDocument();
        expect(screen.queryByText("Elle girildi")).not.toBeInTheDocument();

        await user.clear(field);
        await user.type(field, "33,1");

        expect(await screen.findByText("Elle girildi")).toBeInTheDocument();
        expect(field).toHaveValue("33,1");
    });
});
