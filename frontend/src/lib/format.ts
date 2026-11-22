const currency = new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    maximumFractionDigits: 0,
});

const compactCurrency = new Intl.NumberFormat("tr-TR", {
    style: "currency",
    currency: "TRY",
    notation: "compact",
    maximumFractionDigits: 1,
});

const number = new Intl.NumberFormat("tr-TR");

const date = new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "short", year: "numeric" });

// Grafik kütüphanesi boş veri noktaları için null gönderebilir.
type MaybeNumber = number | null | undefined;
const EMPTY = "–";

export const formatCurrency = (value: MaybeNumber) => (value == null ? EMPTY : currency.format(value));
export const formatCompactCurrency = (value: MaybeNumber) => (value == null ? EMPTY : compactCurrency.format(value));
export const formatNumber = (value: MaybeNumber) => (value == null ? EMPTY : number.format(value));
export const formatDate = (isoDate: string) => date.format(new Date(isoDate));
