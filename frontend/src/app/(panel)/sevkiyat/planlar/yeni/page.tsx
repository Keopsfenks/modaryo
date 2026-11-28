import type { Metadata } from "next";
import PlanEditor from "@/components/shipping/PlanEditor";

export const metadata: Metadata = { title: "Yeni teslimat planı" };

export default function Page() {
    return <PlanEditor />;
}
