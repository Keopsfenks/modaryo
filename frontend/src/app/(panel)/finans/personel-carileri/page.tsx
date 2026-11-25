import type { Metadata } from "next";
import AccountList from "@/components/cari/AccountList";

export const metadata: Metadata = { title: "Personel Carileri" };

export default function EmployeeAccountsPage() {
    return <AccountList role="employee" title="Personel Carileri" />;
}
