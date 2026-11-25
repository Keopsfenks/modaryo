import type { Metadata } from "next";
import Suggestions from "@/components/purchasing/Suggestions";

export const metadata: Metadata = { title: "Sipariş önerileri" };

export default function Page() {
    return <Suggestions />;
}
