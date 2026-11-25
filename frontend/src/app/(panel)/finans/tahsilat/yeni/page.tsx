import type { Metadata } from "next";
import { Suspense } from "react";
import PaymentForm from "@/components/treasury/PaymentForm";

export const metadata: Metadata = { title: "Yeni Tahsilat" };

export default function NewCollectionPage() {
    return (
        <Suspense>
            <PaymentForm kind="collection" />
        </Suspense>
    );
}
