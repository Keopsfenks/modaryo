import type { Metadata } from "next";
import PlanEditor from "@/components/shipping/PlanEditor";

export const metadata: Metadata = { title: "Teslimat planı" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <PlanEditor planId={id} />;
}
