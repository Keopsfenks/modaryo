import type { Metadata } from "next";
import InvoiceEditor from "@/components/invoicing/InvoiceEditor";

export const metadata: Metadata = { title: "Fatura" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <InvoiceEditor group="sales" id={id} />;
}
