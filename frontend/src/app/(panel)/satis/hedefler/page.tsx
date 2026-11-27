import type { Metadata } from "next";
import Targets from "@/components/sales/Targets";

export const metadata: Metadata = { title: "Satış hedefleri" };

export default function Page() {
    return <Targets />;
}
