import type { Metadata } from "next";
import Minimums from "@/components/inventory/Minimums";

export const metadata: Metadata = { title: "Minimum Stok" };

export default function MinimumsPage() {
    return <Minimums />;
}
