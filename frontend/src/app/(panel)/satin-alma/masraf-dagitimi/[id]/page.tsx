import type { Metadata } from "next";
import LandedCostEditor from "@/components/purchasing/LandedCostEditor";

export const metadata: Metadata = { title: "Masraf dağıtımı" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <LandedCostEditor id={id} />;
}
