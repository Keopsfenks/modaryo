import type { Metadata } from "next";
import RatesTable from "@/components/rates/RatesTable";

export const metadata: Metadata = { title: "Döviz kurları" };

export default function Page() {
    return <RatesTable />;
}
