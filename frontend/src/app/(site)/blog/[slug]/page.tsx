import type { Metadata } from "next";
import { notFound } from "next/navigation";
import ArticleLayout from "@/components/site/ArticleLayout";
import { findArticle } from "@/content/site/articles";
import { blogPosts } from "@/content/site/blog";

export function generateStaticParams() {
    return blogPosts.map((a) => ({ slug: a.slug }));
}

export async function generateMetadata({ params }: PageProps<"/blog/[slug]">): Promise<Metadata> {
    const { slug } = await params;
    const found = findArticle(blogPosts, slug);
    if (!found) {
        return {};
    }
    return { title: { absolute: `${found.title} | Modaryo` }, description: found.summary };
}

export default async function BlogArticlePage({ params }: PageProps<"/blog/[slug]">) {
    const { slug } = await params;
    const found = findArticle(blogPosts, slug);
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
