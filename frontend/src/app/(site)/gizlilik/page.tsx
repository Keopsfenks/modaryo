import type { Metadata } from "next";
import LegalPage from "@/components/site/LegalPage";
import Content from "@/content/site/legal/gizlilik.mdx";

export const metadata: Metadata = {
    title: { absolute: "Gizlilik politikası | Modaryo" },
    description: "Modaryo tanıtım sitesinde hangi bilgilerin toplandığı ve nasıl kullanıldığı.",
};

export default function LegalGizlilikPage() {
    return (
        <LegalPage title="Gizlilik politikası">
            <Content />
        </LegalPage>
    );
}
