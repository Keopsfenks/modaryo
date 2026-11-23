import type { MetadataRoute } from "next";
import { blogPosts } from "@/content/site/blog";
import { siteModules } from "@/content/site/modules";
import { siteSectors } from "@/content/site/sectors";
import { siteUrl } from "@/content/site/seo";
import { helpArticles } from "@/content/site/yardim";

const staticPaths = ["", "/ozellikler", "/fiyatlar", "/demo", "/iletisim", "/kvkk", "/gizlilik", "/cerez-politikasi", "/kullanim-sartlari", "/blog", "/yardim"];

export default function sitemap(): MetadataRoute.Sitemap {
    const base = siteUrl();
    const paths = [
        ...staticPaths,
        ...siteModules.map((m) => `/ozellikler/${m.slug}`),
        ...siteSectors.map((s) => `/cozumler/${s.slug}`),
        ...blogPosts.map((p) => `/blog/${p.slug}`),
        ...helpArticles.map((a) => `/yardim/${a.slug}`),
    ];
    return paths.map((path) => ({ url: `${base}${path}` }));
}
