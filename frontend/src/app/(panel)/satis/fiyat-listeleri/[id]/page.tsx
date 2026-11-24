import type { Metadata } from "next";
import PriceListEditor from "@/components/pricing/PriceListEditor";

export const metadata: Metadata = { title: "Fiyat Listesi" };

export default async function PriceListPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <PriceListEditor id={id} />;
}
