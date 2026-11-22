import type { Metadata } from "next";
import AccountList from "@/components/cari/AccountList";

export const metadata: Metadata = { title: "Cari Hesaplar" };

export default function AccountsPage() {
    return <AccountList title="Cari Hesaplar" />;
}
