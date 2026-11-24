import type { Metadata } from "next";
import TransferForm from "@/components/inventory/TransferForm";

export const metadata: Metadata = { title: "Yeni Transfer" };

export default function NewTransferPage() {
    return <TransferForm />;
}
