import type { Metadata } from "next";
import ProductList from "@/components/catalog/ProductList";

export const metadata: Metadata = { title: "Ürünler" };

export default function ProductsPage() {
    return <ProductList title="Ürünler" kind="product" />;
}
