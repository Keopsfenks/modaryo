import type { Article } from "../articles";

export const helpArticles: Article[] = [
    {
        slug: "ornek-rehber",
        title: "Örnek rehber: ilk cari kartınızı oluşturma",
        summary: "Yardım merkezi düzenini göstermek için hazırlanmış örnek bir rehberdir.",
        date: "2026-11-22",
        load: () => import("./ornek-rehber.mdx"),
    },
];
