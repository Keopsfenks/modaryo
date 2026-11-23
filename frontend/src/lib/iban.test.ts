import { describe, expect, it } from "vitest";
import { isValidTrIban } from "./iban";

describe("iban", () => {
    it("isValidTrIban_KnownValidAndInvalid", () => {
        expect(isValidTrIban("TR330006100519786457841326")).toBe(true);
        expect(isValidTrIban("tr33 0006 1005 1978 6457 8413 26")).toBe(true);
        expect(isValidTrIban("TR330006100519786457841327")).toBe(false);
        expect(isValidTrIban("TR3300061005197864578413")).toBe(false);
        expect(isValidTrIban("DE330006100519786457841326")).toBe(false);
    });
});
