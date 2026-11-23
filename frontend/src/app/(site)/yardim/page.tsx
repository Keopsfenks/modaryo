import type { Metadata } from "next";
import ArticleIndex from "@/components/site/ArticleIndex";
import { helpArticles } from "@/content/site/yardim";

export const metadata: Metadata = {
    title: { absolute: "Yardım merkezi | Modaryo" },
    description: "Sık sorulan sorular ve kullanım rehberleri.",
};

export default function HelpIndexPage() {
    return <ArticleIndex title="Yardım merkezi" intro="Sık sorulan sorular ve kullanım rehberleri." basePath="/yardim" items={helpArticles} />;
}
