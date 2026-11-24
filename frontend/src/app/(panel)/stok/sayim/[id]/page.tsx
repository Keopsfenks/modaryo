import type { Metadata } from "next";
import CountSheet from "@/components/inventory/CountSheet";

export const metadata: Metadata = { title: "Sayım" };

export default async function CountPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <CountSheet id={id} />;
}
