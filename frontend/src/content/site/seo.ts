/** Kanonik site adresi (NEXT_PUBLIC_SITE_URL); metadataBase, sitemap, robots ve JSON-LD bunu kullanır. */
export function siteUrl(): string {
    return (process.env.NEXT_PUBLIC_SITE_URL ?? process.env.SITE_URL ?? "http://localhost:3000").replace(/\/+$/, "");
}
