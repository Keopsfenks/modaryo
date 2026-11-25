import type { Metadata } from "next";
import Instruments from "@/components/treasury/Instruments";

export const metadata: Metadata = { title: "Çek / Senet" };

export default function InstrumentsPage() {
    return <Instruments />;
}
