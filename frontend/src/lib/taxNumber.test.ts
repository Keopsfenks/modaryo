import { describe, expect, it } from "vitest";
import { isValidTckn, isValidVkn } from "./taxNumber";

describe("taxNumber", () => {
    it.each(["1234567890", "9876543217"])("isValidVkn_Accepts_%s", (value) => {
        expect(isValidVkn(value)).toBe(true);
    });

    it.each(["1234567891", "123456789", "12345678901", "abcdefghij", ""])("isValidVkn_Rejects_%s", (value) => {
        expect(isValidVkn(value)).toBe(false);
    });

    it("isValidTckn_Accepts", () => {
        expect(isValidTckn("10000000146")).toBe(true);
    });

    it.each(["10000000147", "01234567890", "1000000014", ""])("isValidTckn_Rejects_%s", (value) => {
        expect(isValidTckn(value)).toBe(false);
    });
});
