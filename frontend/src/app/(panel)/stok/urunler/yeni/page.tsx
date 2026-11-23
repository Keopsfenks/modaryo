import type { Metadata } from "next";
import { Suspense } from "react";
import NewProduct from "./NewProduct";

export const metadata: Metadata = { title: "Yeni Ürün" };

export default function NewProductPage() {
    return (
        <Suspense>
            <NewProduct />
        </Suspense>
    );
}
