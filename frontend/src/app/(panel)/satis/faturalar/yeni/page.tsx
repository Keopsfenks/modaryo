import type { Metadata } from "next";
import InvoiceEditor from "@/components/invoicing/InvoiceEditor";

export const metadata: Metadata = { title: "Yeni fatura" };

export default function Page() {
    return <InvoiceEditor group="sales" />;
}
