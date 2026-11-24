import type { Metadata } from "next";
import { Suspense } from "react";
import StockDocuments from "@/components/inventory/StockDocuments";

export const metadata: Metadata = { title: "Stok Fişleri" };

export default function StockDocumentsPage() {
    return (
        <Suspense>
            <StockDocuments />
        </Suspense>
    );
}
