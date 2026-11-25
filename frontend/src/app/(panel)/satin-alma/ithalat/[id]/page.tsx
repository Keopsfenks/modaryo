import type { Metadata } from "next";
import ImportFileDetail from "@/components/purchasing/ImportFileDetail";

export const metadata: Metadata = { title: "İthalat dosyası" };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
    const { id } = await params;
    return <ImportFileDetail id={id} />;
}
