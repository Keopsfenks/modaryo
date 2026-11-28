import type { Metadata } from "next";
import Plans from "@/components/shipping/Plans";

export const metadata: Metadata = { title: "Teslimat planları" };

export default function Page() {
    return <Plans />;
}
