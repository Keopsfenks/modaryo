/** Oturum gerektirmeyen sayfalar (önek eşleşmesi: "/davet" → "/davet/<token>"). */
export const PUBLIC_PATHS = [
    "/giris",
    "/kayit",
    "/sifremi-unuttum",
    "/sifre-sifirla",
    "/eposta-dogrula",
    "/davet",
    // Tanıtım sitesi
    "/ozellikler",
    "/cozumler",
    "/fiyatlar",
    "/demo",
    "/iletisim",
    "/kvkk",
    "/gizlilik",
    "/cerez-politikasi",
    "/kullanim-sartlari",
    "/blog",
    "/yardim",
    "/sitemap.xml",
    "/robots.txt",
];

/** Next.js metadata görselleri: `/opengraph-image-<karma>` (uzantısız, karma ekli). Sosyal ağ botları çerezsiz erişir. */
const METADATA_IMAGE = /^\/(?:opengraph|twitter)-image(?:-[a-z0-9]+)?$/;

/** "/" (tanıtım ana sayfası) yalnızca tam eşleşmeyle herkese açıktır; diğerleri önek eşleşmesidir. */
export function isPublicPath(pathname: string): boolean {
    return pathname === "/" || METADATA_IMAGE.test(pathname) || PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`));
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
