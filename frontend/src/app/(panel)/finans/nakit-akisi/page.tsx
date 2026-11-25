import type { Metadata } from "next";
import CashFlowChart from "@/components/treasury/CashFlowChart";

export const metadata: Metadata = { title: "Nakit Akışı" };

export default function CashFlowPage() {
    return <CashFlowChart />;
}
