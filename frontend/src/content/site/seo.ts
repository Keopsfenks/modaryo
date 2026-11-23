/** Kanonik site adresi; sitemap, robots ve JSON-LD bunu kullanır. */
export function siteUrl(): string {
    return (process.env.SITE_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}
