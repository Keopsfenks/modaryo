import type { Metadata } from "next";
import Shipments from "@/components/sales/Shipments";

export const metadata: Metadata = { title: "Sevkler" };

export default function Page() {
    return <Shipments />;
}
