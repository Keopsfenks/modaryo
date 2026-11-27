import type { Metadata } from "next";
import OrderEditor from "@/components/sales/OrderEditor";

export const metadata: Metadata = { title: "Yeni sipariş" };

export default function Page() {
    return <OrderEditor />;
}
