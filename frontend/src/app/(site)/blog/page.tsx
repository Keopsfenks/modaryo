import type { Metadata } from "next";
import ArticleIndex from "@/components/site/ArticleIndex";
import { blogPosts } from "@/content/site/blog";

export const metadata: Metadata = {
    title: { absolute: "Blog | Modaryo" },
    description: "Rehberler ve ürün notları.",
};

export default function BlogIndexPage() {
    return <ArticleIndex title="Blog" intro="Rehberler ve ürün notları." basePath="/blog" items={blogPosts} />;
}
