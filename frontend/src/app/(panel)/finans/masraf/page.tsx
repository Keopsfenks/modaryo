import type { Metadata } from "next";
import Expenses from "@/components/treasury/Expenses";

export const metadata: Metadata = { title: "Masraflar" };

export default function ExpensesPage() {
    return <Expenses />;
}
