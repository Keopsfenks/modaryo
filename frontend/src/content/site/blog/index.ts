import type { Article } from "../articles";

export const blogPosts: Article[] = [
    {
        slug: "ornek-yazi",
        title: "Örnek yazı: cari hesap takibine nereden başlanır?",
        summary: "Blog altyapısını göstermek için hazırlanmış örnek bir yazıdır.",
        date: "2026-11-22",
        load: () => import("./ornek-yazi.mdx"),
    },
];
