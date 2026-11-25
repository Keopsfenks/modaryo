import type { Metadata } from "next";
import JournalVouchers from "@/components/treasury/JournalVouchers";

export const metadata: Metadata = { title: "Mahsup Fişleri" };

export default function JournalVouchersPage() {
    return <JournalVouchers />;
}
