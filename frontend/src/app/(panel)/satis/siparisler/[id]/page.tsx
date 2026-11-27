import type { Metadata } from "next";
import OrderEditor from "@/components/sales/OrderEditor";

export const metadata: Metadata = { title: "Sipariş" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <OrderEditor id={id} />;
}
