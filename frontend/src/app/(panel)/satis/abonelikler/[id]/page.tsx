import type { Metadata } from "next";
import RecurringEditor from "@/components/sales/RecurringEditor";

export const metadata: Metadata = { title: "Abonelik" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <RecurringEditor id={id === "yeni" ? undefined : id} />;
}
