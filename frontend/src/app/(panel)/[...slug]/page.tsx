import type { Metadata } from "next";
import { notFound } from "next/navigation";
import ComingSoon from "@/components/common/ComingSoon";
import { allNavItems, findNavItem } from "@/config/navigation";

// Menüde olup henüz sayfası yazılmamış modüller için yer tutucu sayfa.
// Menüde olmayan adresler 404 döner.
export const dynamicParams = false;

export function generateStaticParams() {
    return allNavItems
        .filter((item) => item.href !== "/panel" && !item.implemented)
        .map((item) => ({ slug: item.href.split("/").filter(Boolean) }));
}

async function resolveItem(params: Promise<{ slug: string[] }>) {
    const { slug } = await params;
    return findNavItem(`/${slug.join("/")}`);
}

export async function generateMetadata({ params }: PageProps<"/[...slug]">): Promise<Metadata> {
    const item = await resolveItem(params);
    return { title: item?.title ?? "Sayfa bulunamadı" };
}

export default async function ModulePlaceholderPage({ params }: PageProps<"/[...slug]">) {
    const item = await resolveItem(params);
    if (!item) {
        notFound();
    }
    return <ComingSoon title={item.title} />;
}
