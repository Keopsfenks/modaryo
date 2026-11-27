import type { RecurrenceInterval } from "@/lib/api/sales";

const pad = (n: number) => String(n).padStart(2, "0");
const iso = (year: number, month: number, day: number) => `${year}-${pad(month)}-${pad(day)}`;

/**
 * Sonraki üretim tarihleri; sunucudaki RecurrenceCalendar ile aynı kural: başlangıç ayından itibaren
 * dönem adımıyla (1 / 3 / 12 ay) ayın seçilen günü, başlangıç tarihinden önceki günler atlanır, bitiş tarihini aşan üretilmez.
 * `from` verilirse yalnızca o tarihten (dahil) sonrakiler döner.
 */
export function nextDates(interval: RecurrenceInterval, dayOfMonth: number, startDate: string, endDate: string | null, count = 6, from?: string): string[] {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(startDate);
    if (!match || dayOfMonth < 1 || dayOfMonth > 28) return [];
    const step = interval === "monthly" ? 1 : interval === "quarterly" ? 3 : 12;
    const startYear = Number(match[1]);
    const startMonth = Number(match[2]) - 1;
    const floor = from && from > startDate ? from : startDate;
    const result: string[] = [];
    for (let i = 0; i < 2000 && result.length < count; i++) {
        const total = startMonth + i * step;
        const date = iso(startYear + Math.floor(total / 12), (total % 12) + 1, dayOfMonth);
        if (endDate && date > endDate) break;
        if (date >= floor) result.push(date);
    }
    return result;
}
