import type { Metadata } from "next";
import ProductList from "@/components/catalog/ProductList";

export const metadata: Metadata = { title: "Hizmetler" };

export default function ServicesPage() {
    return <ProductList title="Hizmetler" kind="service" />;
}
