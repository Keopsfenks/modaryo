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

const patterns = (maxDecimals: number) => ({
    grouped: new RegExp(`^\\d{1,3}(\\.\\d{3})+(,\\d{1,${maxDecimals}})?$`),
    plainComma: new RegExp(`^\\d+(,\\d{1,${maxDecimals}})?$`),
    plainDot: new RegExp(`^\\d+(\\.\\d{1,${maxDecimals}})?$`),
});
const PATTERNS_2 = patterns(2);
const PATTERNS_4 = patterns(4);

export type ParseAmountOptions = {
    /** Girilebilecek en çok ondalık basamak: 2 (varsayılan, belge tutarları) ya da 4 (birim fiyatlar). */
    maxDecimals?: 2 | 4;
};

/**
 * Türkçe tutar girdisini sayıya çevirir. Virgül ondalık ayracıdır; nokta yalnızca geçerli 3'lü
 * gruplarda binlik ayracıdır ("1.500" → 1500). Virgülsüz tek noktayı izleyen 1-2 hane (birim fiyatlarda
 * 1-4 hane, maxDecimals: 4) ondalıktır ("1500.5"). Boş, geçersiz ya da fazla ondalıklı girdi null döner.
 */
export function parseAmount(input: string, { maxDecimals = 2 }: ParseAmountOptions = {}): number | null {
    const text = input.trim();
    const { grouped, plainComma, plainDot } = maxDecimals === 4 ? PATTERNS_4 : PATTERNS_2;
    if (grouped.test(text)) {
        return Number(text.replace(/\./g, "").replace(",", "."));
    }
    if (plainComma.test(text)) {
        return Number(text.replace(",", "."));
    }
    if (plainDot.test(text)) {
        return Number(text);
    }
    return null;
}

/** Katalog birim fiyatı (alış/satış, varyant fiyat farkı): backend ve Excel 4 ondalık saklar. */
export function parseUnitPrice(input: string): number | null {
    return parseAmount(input, { maxDecimals: 4 });
}
