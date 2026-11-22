// İstemci tarafı doğrulama: mesajlar ve sınırlar backend'le (CommonRules.cs) birebir aynıdır.
// Sunucu yine de son sözü söyler; bunlar yalnızca gereksiz isteği önler.

export type FieldErrors = Record<string, string>;

export function validateEmail(value: string): string | undefined {
    const email = value.trim();
    if (!email) return "E-posta adresi gereklidir.";
    if (email.length > 256) return "E-posta adresi en fazla 256 karakter olabilir.";
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return "Geçerli bir e-posta adresi girin.";
    return undefined;
}

export function validateNewPassword(value: string): string | undefined {
    if (!value) return "Şifre gereklidir.";
    if (value.length < 10) return "Şifre en az 10 karakter olmalıdır.";
    if (value.length > 128) return "Şifre en fazla 128 karakter olabilir.";
    return undefined;
}

export function validateFullName(value: string): string | undefined {
    const name = value.trim();
    if (name.length < 2) return "Ad soyad en az 2 karakter olmalıdır.";
    if (name.length > 100) return "Ad soyad en fazla 100 karakter olabilir.";
    return undefined;
}

export function validateCompanyName(value: string): string | undefined {
    const name = value.trim();
    if (name.length < 2) return "Firma adı en az 2 karakter olmalıdır.";
    if (name.length > 100) return "Firma adı en fazla 100 karakter olabilir.";
    return undefined;
}

export function required(value: string, message: string): string | undefined {
    return value.trim() ? undefined : message;
}

/** Boş olmayan hataları bırakır; sonuç boşsa form geçerlidir. */
export function collectErrors(errors: Record<string, string | undefined>): FieldErrors {
    return Object.fromEntries(Object.entries(errors).filter((entry): entry is [string, string] => Boolean(entry[1])));
}
