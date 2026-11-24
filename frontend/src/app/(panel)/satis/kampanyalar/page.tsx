import type { Metadata } from "next";
import Campaigns from "@/components/pricing/Campaigns";

export const metadata: Metadata = { title: "Kampanyalar" };

export default function CampaignsPage() {
    return <Campaigns />;
}
