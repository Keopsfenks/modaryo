import type { Metadata } from "next";
import OrderList from "@/components/purchasing/OrderList";

export const metadata: Metadata = { title: "Satın alma siparişleri" };

export default function Page() {
    return <OrderList />;
}
