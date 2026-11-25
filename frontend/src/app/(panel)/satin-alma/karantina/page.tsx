import type { Metadata } from "next";
import QuarantineList from "@/components/purchasing/QuarantineList";

export const metadata: Metadata = { title: "Karantina" };

export default function Page() {
    return <QuarantineList />;
}
