import type { Metadata } from "next";
import InvoiceList from "@/components/invoicing/InvoiceList";

export const metadata: Metadata = { title: "Alış faturaları" };

export default function Page() {
    return <InvoiceList group="purchase" />;
}
