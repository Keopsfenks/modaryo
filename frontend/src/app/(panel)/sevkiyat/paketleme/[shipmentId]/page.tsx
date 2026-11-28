import type { Metadata } from "next";
import PackScreen from "@/components/shipping/PackScreen";

export const metadata: Metadata = { title: "Paketleme" };

export default async function Page({ params }: { params: Promise<{ shipmentId: string }> }) {
    const { shipmentId } = await params;
    return <PackScreen shipmentId={shipmentId} />;
}
