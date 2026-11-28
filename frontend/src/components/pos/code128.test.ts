import { describe, expect, it } from "vitest";
import { code128Values, code128Widths, PATTERNS } from "./code128";

describe("code128", () => {
    it("Code128_PatternTableIsConsistent", () => {
        expect(PATTERNS).toHaveLength(107);
        expect(new Set(PATTERNS).size).toBe(107);
        // Her sembol 11 modül, durdurma deseni 13 modüldür.
        PATTERNS.slice(0, 106).forEach((p) => expect(p.split("").reduce((a, c) => a + Number(c), 0)).toBe(11));
        expect(PATTERNS[106].split("").reduce((a, c) => a + Number(c), 0)).toBe(13);
    });

    it("Code128_EncodesKnownValue", () => {
        // Sağlama: (104 + 1 * 33) mod 103.
        expect(code128Values("A")).toEqual([104, 33, (104 + 33) % 103, 106]);
        expect(code128Widths("PS01").reduce((a, c) => a + c, 0)).toBe(11 * 6 + 13);
    });
});
