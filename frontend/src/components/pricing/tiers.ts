import { parseAmount } from "@/lib/money";
import type { PriceTier } from "@/lib/api/pricing";

/** Formda metin olarak tutulan kademe (Türkçe ondalık virgülü). */
export type TierText = { minQty: string; price: string };

export const emptyTiers = (): TierText[] => [{ minQty: "1", price: "" }];

export const numberToText = (n: number): string => String(n).replace(".", ",");

export const toTierText = (tiers: PriceTier[]): TierText[] =>
    tiers.length === 0 ? emptyTiers() : tiers.map((t) => ({ minQty: numberToText(t.minQty), price: numberToText(t.price) }));

const parseValue = (text: string) => parseAmount(text, { maxDecimals: 4 });

/** Kural: ilk kademe 1'den başlar, miktarlar kesin artar, fiyatlar negatif olmaz. Geçerliyse null. */
export function validateTiers(tiers: TierText[]): string | null {
    if (tiers.length === 0) return "En az bir kademe gerekir.";
    let previous = 0;
    for (const [index, tier] of tiers.entries()) {
        const minQty = parseValue(tier.minQty);
        const price = parseValue(tier.price);
        if (minQty === null || minQty <= 0) return `${index + 1}. kademenin miktarı geçerli bir sayı olmalı.`;
        if (price === null) return `${index + 1}. kademenin fiyatı 0 veya daha büyük bir sayı olmalı (en çok 4 ondalık).`;
        if (index === 0 && minQty !== 1) return "İlk kademe 1 miktarından başlamalı.";
        if (index > 0 && minQty <= previous) return "Kademe miktarları artan sırada olmalı.";
        previous = minQty;
    }
    return null;
}

/** Kademeler geçerliyse sayıya çevirir, değilse null. */
export function parseTiers(tiers: TierText[]): PriceTier[] | null {
    if (validateTiers(tiers)) return null;
    return tiers.map((t) => ({ minQty: parseValue(t.minQty)!, price: parseValue(t.price)! }));
}

const money = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
const qty = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 4 });

export const formatPrice = (n: number): string => money.format(n);

/** "1+: 100,00 · 10+: 90,00"; tek kademede yalnızca fiyat. */
export function summarizeTiers(tiers: PriceTier[]): string {
    if (tiers.length === 1) return formatPrice(tiers[0].price);
    return tiers.map((t) => `${qty.format(t.minQty)}+: ${formatPrice(t.price)}`).join(" · ");
}
