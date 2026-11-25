import type { Metadata } from "next";
import AgingReport from "@/components/treasury/AgingReport";

export const metadata: Metadata = { title: "Yaşlandırma" };

export default function AgingPage() {
    return <AgingReport />;
}
