import { describe, expect, it } from "vitest";
import { blogPosts } from "@/content/site/blog";
import { helpArticles } from "@/content/site/yardim";
import { siteModules } from "@/content/site/modules";
import { siteSectors } from "@/content/site/sectors";
import robots from "./robots";
import sitemap from "./sitemap";

const BASE = "http://localhost:3000";

describe("seo", () => {
    it("Sitemap_ContainsAllContentSlugs", () => {
        const urls = sitemap().map((e) => e.url);

        for (const path of ["/", "/ozellikler", "/fiyatlar", "/demo", "/iletisim", "/kvkk", "/gizlilik", "/cerez-politikasi", "/kullanim-sartlari", "/blog", "/yardim"]) {
            expect(urls).toContain(path === "/" ? BASE : `${BASE}${path}`);
        }
        for (const m of siteModules) {
            expect(urls).toContain(`${BASE}/ozellikler/${m.slug}`);
        }
        for (const s of siteSectors) {
            expect(urls).toContain(`${BASE}/cozumler/${s.slug}`);
        }
        for (const p of blogPosts) {
            expect(urls).toContain(`${BASE}/blog/${p.slug}`);
        }
        for (const a of helpArticles) {
            expect(urls).toContain(`${BASE}/yardim/${a.slug}`);
        }
        expect(new Set(urls).size).toBe(urls.length);
    });

    it("Robots_DisallowsPanelAndApi", () => {
        const r = robots();
        const rule = Array.isArray(r.rules) ? r.rules[0] : r.rules;

        expect(rule.allow).toBe("/");
        expect(rule.disallow).toEqual(["/panel", "/ayarlar", "/api/", "/kurulum"]);
        expect(r.sitemap).toBe(`${BASE}/sitemap.xml`);
    });
});
