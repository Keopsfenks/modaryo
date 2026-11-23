/** JSON-LD betiği. "<" kaçırılır, böylece içerik betik bloğundan çıkamaz. */
export default function JsonLd({ data }: { data: Record<string, unknown> }) {
    return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(data).replace(/</g, "\\u003c") }} />;
}

export function breadcrumbJsonLd(base: string, trail: { name: string; path: string }[]) {
    return {
        "@context": "https://schema.org",
        "@type": "BreadcrumbList",
        itemListElement: trail.map((t, i) => ({ "@type": "ListItem", position: i + 1, name: t.name, item: `${base}${t.path}` })),
    };
}

export function softwareApplicationJsonLd(base: string) {
    return {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        name: "Modaryo ERP",
        applicationCategory: "BusinessApplication",
        operatingSystem: "Web",
        url: base,
        description: "Cari, stok, satış ve muhasebe işlerini tek panelde toplayan bulut ERP.",
        offers: { "@type": "Offer", price: "0", priceCurrency: "TRY" },
    };
}
