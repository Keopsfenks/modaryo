export type Currency = "TRY" | "USD" | "EUR" | "GBP";

export const CURRENCIES: readonly Currency[] = ["TRY", "USD", "EUR", "GBP"];

const decimal = new Intl.NumberFormat("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const foreign: Record<Exclude<Currency, "TRY">, Intl.NumberFormat> = {
    USD: new Intl.NumberFormat("tr-TR", { style: "currency", currency: "USD" }),
    EUR: new Intl.NumberFormat("tr-TR", { style: "currency", currency: "EUR" }),
    GBP: new Intl.NumberFormat("tr-TR", { style: "currency", currency: "GBP" }),
};

// Intl bazı sürümlerde boşluk yerine NBSP / dar NBSP üretir; tek tip düz boşluğa indirgenir.
const normalize = (text: string) => text.replace(/[  ]/g, " ");

/**
 * tr-TR, her zaman 2 ondalık. TL sembolü tutardan sonra gelir ("1.234,50 ₺"),
 * döviz sembolleri Intl çıktısındaki gibi başta durur ("$500,00").
 */
export function formatMoney(amount: number, currency: Currency): string {
    if (currency === "TRY") {
        return normalize(`${decimal.format(amount)} ₺`);
    }
    return normalize(foreign[currency].format(amount));
}

/** Bakiye > 0 → "(B)" borç, < 0 → mutlak değer + "(A)" alacak, 0 → ek yok. */
export function formatBalance(balance: number, currency: Currency): string {
    if (balance > 0) {
        return `${formatMoney(balance, currency)} (B)`;
    }
    if (balance < 0) {
        return `${formatMoney(Math.abs(balance), currency)} (A)`;
    }
    return formatMoney(0, currency);
}

const GROUPED = /^\d{1,3}(\.\d{3})+(,\d{1,2})?$/;
const PLAIN_COMMA = /^\d+(,\d{1,2})?$/;
const PLAIN_DOT = /^\d+(\.\d{1,2})?$/;

/**
 * Türkçe tutar girdisini sayıya çevirir. Virgül ondalık ayracıdır; nokta yalnızca geçerli 3'lü
 * gruplarda binlik ayracıdır ("1.500" → 1500). Virgülsüz tek noktayı izleyen 1-2 hane ondalıktır
 * ("1500.5"). Boş, geçersiz ya da 2'den fazla ondalıklı girdi null döner.
 */
export function parseAmount(input: string): number | null {
    const text = input.trim();
    if (GROUPED.test(text)) {
        return Number(text.replace(/\./g, "").replace(",", "."));
    }
    if (PLAIN_COMMA.test(text)) {
        return Number(text.replace(",", "."));
    }
    if (PLAIN_DOT.test(text)) {
        return Number(text);
    }
    return null;
}
