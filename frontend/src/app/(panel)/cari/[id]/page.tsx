import type { Metadata } from "next";
import { Suspense } from "react";
import AccountDetail from "./AccountDetail";

export const metadata: Metadata = { title: "Cari Detayı" };

export default async function AccountDetailPage({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return (
        <Suspense>
            <AccountDetail id={id} />
        </Suspense>
    );
}
