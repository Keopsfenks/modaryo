import type { Metadata } from "next";
import QuoteList from "@/components/sales/QuoteList";

export const metadata: Metadata = { title: "Teklifler" };

export default function Page() {
    return <QuoteList />;
}
