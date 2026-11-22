import type { Metadata } from "next";
import AccountList from "@/components/cari/AccountList";

export const metadata: Metadata = { title: "Tedarikçiler" };

export default function SuppliersPage() {
    return <AccountList title="Tedarikçiler" role="supplier" />;
}
