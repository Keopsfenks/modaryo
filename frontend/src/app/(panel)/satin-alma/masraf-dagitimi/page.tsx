import type { Metadata } from "next";
import LandedCostList from "@/components/purchasing/LandedCostList";

export const metadata: Metadata = { title: "Masraf dağıtımı" };

export default function Page() {
    return <LandedCostList />;
}
