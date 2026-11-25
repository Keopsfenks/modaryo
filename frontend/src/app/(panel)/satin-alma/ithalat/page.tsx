import type { Metadata } from "next";
import ImportFiles from "@/components/purchasing/ImportFiles";

export const metadata: Metadata = { title: "İthalat dosyaları" };

export default function Page() {
    return <ImportFiles />;
}
