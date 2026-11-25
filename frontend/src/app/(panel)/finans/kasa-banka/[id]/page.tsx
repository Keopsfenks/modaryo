import type { Metadata } from "next";
import AccountMovements from "@/components/treasury/AccountMovements";

export const metadata: Metadata = { title: "Hesap Hareketleri" };

export default async function TreasuryAccountPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <AccountMovements id={id} />;
}
