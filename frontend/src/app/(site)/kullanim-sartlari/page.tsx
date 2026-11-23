import type { Metadata } from "next";
import LegalPage from "@/components/site/LegalPage";
import Content from "@/content/site/legal/kullanim-sartlari.mdx";

export const metadata: Metadata = {
    title: { absolute: "Kullanım şartları | Modaryo" },
    description: "Modaryo sitesi ve hizmetinin kullanım şartları.",
};

export default function LegalKullanimSartlariPage() {
    return (
        <LegalPage title="Kullanım şartları">
            <Content />
        </LegalPage>
    );
}
