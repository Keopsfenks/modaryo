import type { Metadata } from "next";
import RecurringList from "@/components/sales/RecurringList";

export const metadata: Metadata = { title: "Abonelikler" };

export default function Page() {
    return <RecurringList />;
}
