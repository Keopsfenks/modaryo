import type { Metadata } from "next";
import StockStatus from "@/components/inventory/StockStatus";

export const metadata: Metadata = { title: "Stok Durumu" };

export default function StockStatusPage() {
    return <StockStatus />;
}
