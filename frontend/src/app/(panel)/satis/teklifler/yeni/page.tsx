import type { Metadata } from "next";
import QuoteEditor from "@/components/sales/QuoteEditor";

export const metadata: Metadata = { title: "Yeni teklif" };

export default function Page() {
    return <QuoteEditor />;
}
