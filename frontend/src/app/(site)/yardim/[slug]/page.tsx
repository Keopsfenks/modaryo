import type { Metadata } from "next";
import { notFound } from "next/navigation";
import ArticleLayout from "@/components/site/ArticleLayout";
import { findArticle } from "@/content/site/articles";
import { helpArticles } from "@/content/site/yardim";

export function generateStaticParams() {
    return helpArticles.map((a) => ({ slug: a.slug }));
}

export async function generateMetadata({ params }: PageProps<"/yardim/[slug]">): Promise<Metadata> {
    const { slug } = await params;
    const found = findArticle(helpArticles, slug);
    if (!found) {
        return {};
    }
    return { title: { absolute: `${found.title} | Modaryo` }, description: found.summary };
}

export default async function HelpArticlePage({ params }: PageProps<"/yardim/[slug]">) {
    const { slug } = await params;
    const found = findArticle(helpArticles, slug);
    if (!found) {
        notFound();
    }
    const { default: Content } = await found.load();
    return (
        <ArticleLayout title={found.title} date={found.date}>
            <Content />
        </ArticleLayout>
    );
}
