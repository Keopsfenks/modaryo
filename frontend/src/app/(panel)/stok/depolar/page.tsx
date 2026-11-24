import type { Metadata } from "next";
import Warehouses from "@/components/inventory/Warehouses";

export const metadata: Metadata = { title: "Depolar" };

export default function WarehousesPage() {
    return <Warehouses />;
}
