import type { Metadata } from "next";
import { Suspense } from "react";
import ItemCard from "@/components/inventory/ItemCard";

export const metadata: Metadata = { title: "Stok Hareketleri" };

export default function ItemCardPage() {
    return (
        <Suspense>
            <ItemCard />
        </Suspense>
    );
}
