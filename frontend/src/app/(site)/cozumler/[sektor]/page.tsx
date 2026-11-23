import type { Metadata } from "next";
import { notFound } from "next/navigation";
import SectorTemplate from "@/components/site/SectorTemplate";
import { getSector, siteSectors } from "@/content/site/sectors";

export function generateStaticParams() {
    return siteSectors.map((s) => ({ sektor: s.slug }));
}

export async function generateMetadata({ params }: PageProps<"/cozumler/[sektor]">): Promise<Metadata> {
    const { sektor } = await params;
    const found = getSector(sektor);
    if (!found) {
        return {};
    }
    return { title: { absolute: `${found.name} | Modaryo` }, description: found.summary };
}

export default async function SectorPage({ params }: PageProps<"/cozumler/[sektor]">) {
    const { sektor } = await params;
    const found = getSector(sektor);
    if (!found) {
        notFound();
    }
    return <SectorTemplate sector={found} />;
}
