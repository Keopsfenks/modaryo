import { formatMoney } from "@/lib/money";

export const money = (n: number): string => formatMoney(n, "TRY");
export const round2 = (n: number): number => Math.round((n + Number.EPSILON) * 100) / 100;
export const fmtQty = (n: number): string => n.toLocaleString("tr-TR", { maximumFractionDigits: 3, minimumFractionDigits: 0, useGrouping: false });

/** Kasada ondalık girdi: "1,5" ve "1.5" kabul edilir; geçersiz ya da boşsa null. */
export function parseNumber(text: string): number | null {
    const t = text.trim().replace(",", ".");
    if (t === "" || !/^\d+(\.\d+)?$/.test(t)) return null;
    return Number(t);
}
