import type { Metadata } from "next";
import ReceiptForm from "@/components/purchasing/ReceiptForm";

export const metadata: Metadata = { title: "Yeni mal kabul" };

export default async function Page({ searchParams }: { searchParams: Promise<{ po?: string }> }) {
    const { po } = await searchParams;
    return <ReceiptForm poId={po ?? null} />;
}
