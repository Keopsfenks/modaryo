import type { Metadata } from "next";
import PendingOrders from "@/components/sales/PendingOrders";

export const metadata: Metadata = { title: "Bekleyen siparişler" };

export default function Page() {
    return <PendingOrders />;
}
