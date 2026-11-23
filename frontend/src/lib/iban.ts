// services/erp CompanyProfileSettings.IsValidIban karşılığı: "TR" + 24 rakam, mod-97 === 1. Boşluklar yok sayılır.

export function normalizeIban(iban: string): string {
    return iban.replace(/\s+/g, "").toUpperCase();
}

export function isValidTrIban(iban: string): boolean {
    const value = normalizeIban(iban);
    if (!/^TR[0-9]{24}$/.test(value)) {
        return false;
    }

    const rearranged = `${value.slice(4)}2927${value.slice(2, 4)}`; // T=29, R=27
    let remainder = 0;
    for (const c of rearranged) {
        remainder = (remainder * 10 + (c.charCodeAt(0) - 48)) % 97;
    }
    return remainder === 1;
}
