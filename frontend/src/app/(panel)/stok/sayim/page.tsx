import type { Metadata } from "next";
import CountList from "@/components/inventory/CountList";

export const metadata: Metadata = { title: "Stok Sayımı" };

export default function CountsPage() {
    return <CountList />;
}
