import type { Metadata } from "next";
import JournalExport from "@/components/treasury/JournalExport";

export const metadata: Metadata = { title: "Muhasebe Aktarımı" };

export default function JournalExportPage() {
    return <JournalExport />;
}
