import type { Metadata } from "next";
import InvoiceList from "@/components/invoicing/InvoiceList";

export const metadata: Metadata = { title: "Faturalar" };

export default function Page() {
    return <InvoiceList group="sales" />;
}
