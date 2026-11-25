import type { Metadata } from "next";
import Accounts from "@/components/treasury/Accounts";

export const metadata: Metadata = { title: "Kasa & Banka" };

export default function TreasuryAccountsPage() {
    return <Accounts />;
}
