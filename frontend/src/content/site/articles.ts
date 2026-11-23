import type { ComponentType } from "react";

export type Article = {
    slug: string;
    title: string;
    summary: string;
    /** ISO tarih (yyyy-mm-dd). */
    date: string;
    load: () => Promise<{ default: ComponentType }>;
};

export function findArticle(list: Article[], slug: string): Article | undefined {
    return list.find((a) => a.slug === slug);
}
