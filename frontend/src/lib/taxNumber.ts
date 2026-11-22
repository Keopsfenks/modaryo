// services/erp/src/Domain/CurrentAccounts/TaxNumber.cs algoritmalarının birebir karşılığı.

const isDigits = (value: string, length: number) => value.length === length && /^[0-9]+$/.test(value);

const digit = (value: string, index: number) => value.charCodeAt(index) - 48;

/** 10 haneli vergi kimlik numarası (VKN) sağlama toplamı. */
export function isValidVkn(value: string): boolean {
    if (!isDigits(value, 10)) {
        return false;
    }

    let sum = 0;
    for (let i = 0; i < 9; i++) {
        const t = (digit(value, i) + 9 - i) % 10;
        let r = (t * (1 << (9 - i))) % 9;
        if (t !== 0 && r === 0) {
            r = 9;
        }
        sum += r;
    }

    return (10 - (sum % 10)) % 10 === digit(value, 9);
}

/** 11 haneli T.C. kimlik numarası (TCKN) sağlama toplamı. */
export function isValidTckn(value: string): boolean {
    if (!isDigits(value, 11) || value[0] === "0") {
        return false;
    }

    const odd = digit(value, 0) + digit(value, 2) + digit(value, 4) + digit(value, 6) + digit(value, 8);
    const even = digit(value, 1) + digit(value, 3) + digit(value, 5) + digit(value, 7);
    const d10 = (((odd * 7 - even) % 10) + 10) % 10;
    if (d10 !== digit(value, 9)) {
        return false;
    }

    let total = 0;
    for (let i = 0; i < 10; i++) {
        total += digit(value, i);
    }

    return total % 10 === digit(value, 10);
}
