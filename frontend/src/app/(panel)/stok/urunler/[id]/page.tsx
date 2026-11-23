import type { Metadata } from "next";
import { Suspense } from "react";
import ProductDetail from "./ProductDetail";

export const metadata: Metadata = { title: "Ürün Detayı" };

export default async function ProductDetailPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return (
        <Suspense>
            <ProductDetail id={id} />
        </Suspense>
    );
}
