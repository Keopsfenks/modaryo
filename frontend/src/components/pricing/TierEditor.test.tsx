import { screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import TierEditor from "./TierEditor";
import { parseTiers, validateTiers, type TierText } from "./tiers";

function Harness({ initial }: { initial: TierText[] }) {
    const [tiers, setTiers] = useState(initial);
    return <TierEditor tiers={tiers} onChange={setTiers} unit="adet" />;
}

describe("TierEditor", () => {
    it("TierEditor_RejectsNonIncreasing", async () => {
        const { user } = renderWithTheme(
            <Harness
                initial={[
                    { minQty: "1", price: "100" },
                    { minQty: "10", price: "90" },
                ]}
            />,
        );
        expect(screen.queryByRole("alert")).not.toBeInTheDocument();

        const second = screen.getByLabelText("2. kademe miktarı");
        await user.clear(second);
        await user.type(second, "1");

        expect(screen.getByRole("alert")).toHaveTextContent("Kademe miktarları artan sırada olmalı.");
    });

    it("TierEditor_AddsTierAndFirstMustBeOne", async () => {
        const { user } = renderWithTheme(<Harness initial={[{ minQty: "1", price: "100" }]} />);

        await user.click(screen.getByRole("button", { name: "Kademe ekle" }));
        expect(screen.getByLabelText("2. kademe miktarı")).toHaveValue("2");

        const first = screen.getByLabelText("1. kademe miktarı");
        await user.clear(first);
        await user.type(first, "5");
        expect(screen.getByRole("alert")).toHaveTextContent("İlk kademe 1 miktarından başlamalı.");
    });

    it("tiers_ValidateAndParse", () => {
        expect(validateTiers([{ minQty: "1", price: "100,5" }])).toBeNull();
        expect(validateTiers([{ minQty: "1", price: "" }])).toMatch(/fiyatı/);
        expect(parseTiers([{ minQty: "1", price: "100,5" }, { minQty: "10", price: "90" }])).toEqual([
            { minQty: 1, price: 100.5 },
            { minQty: 10, price: 90 },
        ]);
        expect(parseTiers([{ minQty: "2", price: "1" }])).toBeNull();
    });
});
