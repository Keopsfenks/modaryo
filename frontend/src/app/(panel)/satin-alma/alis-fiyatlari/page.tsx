import type { Metadata } from "next";
import PriceLists from "@/components/pricing/PriceLists";

export const metadata: Metadata = { title: "Alış Fiyatları" };

export default function PurchasePricesPage() {
    return <PriceLists kind="purchase" title="Alış fiyatları" />;
}
