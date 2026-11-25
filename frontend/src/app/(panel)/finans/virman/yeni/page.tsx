import type { Metadata } from "next";
import TransferForm from "@/components/treasury/TransferForm";

export const metadata: Metadata = { title: "Yeni Virman" };

export default function NewTransferPage() {
    return <TransferForm />;
}
