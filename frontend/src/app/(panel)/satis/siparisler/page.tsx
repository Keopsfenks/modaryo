import type { Metadata } from "next";
import OrderList from "@/components/sales/OrderList";

export const metadata: Metadata = { title: "Siparişler" };

export default function Page() {
    return <OrderList />;
}
