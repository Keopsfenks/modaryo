import { describe, expect, it } from "vitest";
import { formatBalance, formatMoney } from "./money";

describe("money", () => {
    it("formatBalance_TwoDecimals", () => {
        expect(formatBalance(0.3, "TRY")).toBe("0,30 ₺ (B)");
    });

    it("formatBalance_Negative", () => {
        expect(formatBalance(-500, "USD")).toBe("$500,00 (A)");
    });

    it("formatBalance_Zero", () => {
        expect(formatBalance(0, "TRY")).toBe("0,00 ₺");
    });

    it("formatBalance_Thousands", () => {
        expect(formatBalance(184500, "TRY")).toBe("184.500,00 ₺ (B)");
    });

    it("formatMoney_OtherCurrencies", () => {
        expect(formatMoney(1234.5, "EUR")).toBe("€1.234,50");
        expect(formatMoney(10, "GBP")).toBe("£10,00");
    });

    it("formatMoney_DoesNotContainNbsp", () => {
        expect(formatMoney(1, "TRY")).not.toMatch(/\u00a0|\u202f/);
    });
});
