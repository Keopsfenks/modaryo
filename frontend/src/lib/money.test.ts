import { describe, expect, it } from "vitest";
import { formatBalance, formatMoney, parseAmount } from "./money";

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

    it("parseAmount_TurkishFormats", () => {
        expect(parseAmount("1.500")).toBe(1500);
        expect(parseAmount("1.500,50")).toBe(1500.5);
        expect(parseAmount("1500,5")).toBe(1500.5);
        expect(parseAmount("1500")).toBe(1500);
        expect(parseAmount("1500.5")).toBe(1500.5);
        expect(parseAmount("1.234.567,89")).toBe(1234567.89);
        expect(parseAmount("  25 ")).toBe(25);
    });

    it("parseAmount_Invalid", () => {
        expect(parseAmount("1.5,3")).toBeNull();
        expect(parseAmount("1,500,00")).toBeNull();
        expect(parseAmount("abc")).toBeNull();
        expect(parseAmount("")).toBeNull();
        expect(parseAmount("1,234")).toBeNull();
        expect(parseAmount("1.234,567")).toBeNull();
        expect(parseAmount("10.123")).toBe(10123);
        expect(parseAmount("-5")).toBeNull();
    });
});
