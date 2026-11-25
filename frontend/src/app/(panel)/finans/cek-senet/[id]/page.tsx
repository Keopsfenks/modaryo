import type { Metadata } from "next";
import InstrumentDetail from "@/components/treasury/InstrumentDetail";

export const metadata: Metadata = { title: "Çek / Senet Kartı" };

export default async function InstrumentPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <InstrumentDetail id={id} />;
}
