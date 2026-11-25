import type { Metadata } from "next";
import ReceiptList from "@/components/purchasing/ReceiptList";

export const metadata: Metadata = { title: "Mal kabul" };

export default function Page() {
    return <ReceiptList />;
}
