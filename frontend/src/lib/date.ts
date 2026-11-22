const pad = (n: number) => String(n).padStart(2, "0");

/** Yerel tarihi "YYYY-MM-DD" olarak verir. */
export function todayIso(): string {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** "YYYY-MM-DD" + gün sayısı → "YYYY-MM-DD". Geçersiz girdide boş string. */
export function addDaysIso(iso: string, days: number): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
    if (!match) return "";
    const d = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + days);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** "YYYY-MM-DD" → "dd.MM.yyyy". Boş girdide boş string, beklenmeyen biçimde girdiyi olduğu gibi döndürür. */
export function formatDate(iso: string | null | undefined): string {
    if (!iso) return "";
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
    return match ? `${match[3]}.${match[2]}.${match[1]}` : iso;
}
