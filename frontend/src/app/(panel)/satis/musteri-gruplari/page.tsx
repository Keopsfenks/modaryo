import type { Metadata } from "next";
import CustomerGroups from "@/components/pricing/CustomerGroups";

export const metadata: Metadata = { title: "Müşteri Grupları" };

export default function CustomerGroupsPage() {
    return <CustomerGroups />;
}
