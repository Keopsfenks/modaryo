import type { Metadata } from "next";
import AccountList from "@/components/cari/AccountList";

export const metadata: Metadata = { title: "Müşteriler" };

export default function CustomersPage() {
    return <AccountList title="Müşteriler" role="customer" />;
}
