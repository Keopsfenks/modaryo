import type { Metadata } from "next";
import { Suspense } from "react";
import PaymentForm from "@/components/treasury/PaymentForm";

export const metadata: Metadata = { title: "Yeni Ödeme" };

export default function NewPaymentPage() {
    return (
        <Suspense>
            <PaymentForm kind="payment" />
        </Suspense>
    );
}
