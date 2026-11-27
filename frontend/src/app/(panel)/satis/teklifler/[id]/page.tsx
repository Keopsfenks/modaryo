import type { Metadata } from "next";
import QuoteEditor from "@/components/sales/QuoteEditor";

export const metadata: Metadata = { title: "Teklif" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <QuoteEditor id={id} />;
}
