import type { Metadata } from "next";
import ExchangeDifferences from "@/components/treasury/ExchangeDifferences";

export const metadata: Metadata = { title: "Kur Farkı" };

export default function ExchangeDifferencesPage() {
    return <ExchangeDifferences />;
}
