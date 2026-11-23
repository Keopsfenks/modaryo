import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithTheme } from "@/test/render";
import Cookie from "./cerez-politikasi/page";
import Privacy from "./gizlilik/page";
import Kvkk from "./kvkk/page";
import Terms from "./kullanim-sartlari/page";
import BlogPost, { generateStaticParams as blogParams } from "./blog/[slug]/page";
import HelpArticle, { generateStaticParams as helpParams } from "./yardim/[slug]/page";
import { SAMPLE_NOTICE } from "@/components/site/ArticleLayout";

describe("legal and article pages", () => {
    it("LegalPages_ShowDraftNotice", async () => {
        for (const Page of [Kvkk, Privacy, Cookie, Terms]) {
            const { unmount } = renderWithTheme(await Page());
            expect(screen.getByText("Bu metin taslaktır; yayın öncesi hukuki kontrolden geçirilmelidir.")).toBeInTheDocument();
            expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
            unmount();
        }
    });

    it("SampleArticles_AreMarkedAsSample", async () => {
        const [{ slug: blogSlug }] = await blogParams();
        renderWithTheme(await BlogPost({ params: Promise.resolve({ slug: blogSlug }) } as unknown as Parameters<typeof BlogPost>[0]));
        expect(screen.getByText(SAMPLE_NOTICE)).toBeInTheDocument();
        expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    });

    it("SampleHelpArticle_AndUnknownSlug", async () => {
        const [{ slug }] = await helpParams();
        renderWithTheme(await HelpArticle({ params: Promise.resolve({ slug }) } as unknown as Parameters<typeof HelpArticle>[0]));
        expect(screen.getByText(SAMPLE_NOTICE)).toBeInTheDocument();
        await expect(HelpArticle({ params: Promise.resolve({ slug: "yok" }) } as unknown as Parameters<typeof HelpArticle>[0])).rejects.toThrow();
    });
});
