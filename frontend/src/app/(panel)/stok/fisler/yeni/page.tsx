import type { Metadata } from "next";
import StockDocumentForm from "@/components/inventory/StockDocumentForm";

export const metadata: Metadata = { title: "Yeni Stok Fişi" };

export default function NewStockDocumentPage() {
    return <StockDocumentForm />;
}
