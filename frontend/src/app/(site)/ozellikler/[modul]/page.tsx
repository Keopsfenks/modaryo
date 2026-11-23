import type { Metadata } from "next";
import { notFound } from "next/navigation";
import FeatureTemplate from "@/components/site/FeatureTemplate";
import { getModule, siteModules } from "@/content/site/modules";

export function generateStaticParams() {
    return siteModules.map((m) => ({ modul: m.slug }));
}

export async function generateMetadata({ params }: PageProps<"/ozellikler/[modul]">): Promise<Metadata> {
    const { modul } = await params;
    const found = getModule(modul);
    if (!found) {
        return {};
    }
    return { title: { absolute: `${found.name} | Modaryo` }, description: found.summary };
}

export default async function FeaturePage({ params }: PageProps<"/ozellikler/[modul]">) {
    const { modul } = await params;
    const found = getModule(modul);
    if (!found) {
        notFound();
    }
    return <FeatureTemplate module={found} />;
}
