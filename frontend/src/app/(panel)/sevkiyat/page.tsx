import type { Metadata } from "next";
import ShippingBoard from "@/components/shipping/ShippingBoard";

export const metadata: Metadata = { title: "Sevkiyat" };

export default function Page() {
    return <ShippingBoard />;
}
