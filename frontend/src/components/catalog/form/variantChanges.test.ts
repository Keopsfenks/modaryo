import { describe, expect, it } from "vitest";
import type { VariantDto } from "@/lib/api/catalog";
import { variantsToRemove } from "./variantChanges";

const variant = (id: string, values: Record<string, string>): VariantDto => ({
    id,
    code: id,
    barcode: null,
    values,
    salePriceOverride: null,
    purchasePriceOverride: null,
    isActive: true,
    inUse: false,
});

const variants = [variant("a", { Renk: "Kırmızı", Beden: "S" }), variant("b", { Renk: "Mavi", Beden: "S" })];

describe("variantsToRemove", () => {
    it("variantsToRemove_SameSets_RemovesNothing", () => {
        expect(variantsToRemove(variants, [{ name: "Beden", values: ["S"] }, { name: "Renk", values: ["Mavi", "Kırmızı", "Yeşil"] }])).toEqual([]);
    });

    it("variantsToRemove_DroppedValue_RemovesOnlyThatVariant", () => {
        expect(variantsToRemove(variants, [{ name: "Renk", values: ["Mavi"] }, { name: "Beden", values: ["S"] }]).map((v) => v.id)).toEqual(["a"]);
    });

    it("variantsToRemove_RenamedAttribute_RemovesAll", () => {
        expect(variantsToRemove(variants, [{ name: "Renkler", values: ["Mavi", "Kırmızı"] }, { name: "Beden", values: ["S"] }])).toHaveLength(2);
    });

    it("variantsToRemove_NewAttribute_RemovesAll", () => {
        expect(variantsToRemove(variants, [{ name: "Renk", values: ["Mavi", "Kırmızı"] }, { name: "Beden", values: ["S"] }, { name: "Boy", values: ["1"] }])).toHaveLength(2);
    });

    it("variantsToRemove_NoAttributes_RemovesAll", () => {
        expect(variantsToRemove(variants, [])).toHaveLength(2);
    });
});
