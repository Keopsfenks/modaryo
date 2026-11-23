import type { Metadata } from "next";
import ImportWizard from "@/components/catalog/ImportWizard";
import PageHeader from "@/components/common/PageHeader";

export const metadata: Metadata = { title: "Ürünleri İçe Aktar" };

export default function ImportPage() {
    return (
        <>
            <PageHeader title="Ürünleri İçe Aktar" subtitle="Excel dosyasından toplu ürün ve hizmet kartı oluşturun veya güncelleyin" />
            <ImportWizard />
        </>
    );
}
