import type { Metadata } from "next";
import LandedCostEditor from "@/components/purchasing/LandedCostEditor";

export const metadata: Metadata = { title: "Yeni masraf dağıtımı" };

export default async function Page({ searchParams }: { searchParams: Promise<{ importFile?: string }> }) {
    const { importFile } = await searchParams;
    return <LandedCostEditor initialImportFileId={importFile ?? null} />;
}
