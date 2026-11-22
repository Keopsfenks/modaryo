import type { Metadata } from "next";
import DueOverview from "./DueOverview";

export const metadata: Metadata = { title: "Vade Takibi" };

export default function DueTrackingPage() {
    return <DueOverview />;
}
