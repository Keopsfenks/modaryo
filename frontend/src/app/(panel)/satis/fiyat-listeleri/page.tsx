import type { Metadata } from "next";
import PriceLists from "@/components/pricing/PriceLists";

export const metadata: Metadata = { title: "Fiyat Listeleri" };

export default function PriceListsPage() {
    return <PriceLists kind="sale" title="Fiyat listeleri" />;
}
