/** Oturum gerektirmeyen sayfalar (önek eşleşmesi: "/davet" → "/davet/<token>"). */
export const PUBLIC_PATHS = ["/giris", "/kayit", "/sifremi-unuttum", "/sifre-sifirla", "/eposta-dogrula", "/davet"];

export function isPublicPath(pathname: string): boolean {
    return PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`));
}

/**
 * Girişten sonra gidilecek adres. Yalnızca site içi göreli yollar kabul edilir; dış adresler
 * ("https://…", "//…", "/\…") açık yönlendirmeye karşı "/panel" olur.
 */
export function safeNext(value: string | null | undefined): string {
    if (!value || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) {
        return "/panel";
    }

    return value;
}

/**
 * Oturum açıldıktan sonra gidilecek yer: güvenli "next". Firması olmayan kullanıcı başlangıç ekranına gider;
 * yalnızca bir davet bağlantısından geliyorsa oraya döner (daveti kabul edebilsin).
 */
export function afterSignInPath(session: { activeCompany: unknown }, next: string | null | undefined): string {
    const target = safeNext(next);
    if (session.activeCompany || target.startsWith("/davet/")) {
        return target;
    }

    return "/baslangic";
}

export function loginUrl(nextPath: string): string {
    return `/giris?next=${encodeURIComponent(nextPath)}`;
}
