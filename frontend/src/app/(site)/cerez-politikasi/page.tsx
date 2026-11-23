import type { Metadata } from "next";
import LegalPage from "@/components/site/LegalPage";
import Content from "@/content/site/legal/cerez-politikasi.mdx";

export const metadata: Metadata = {
    title: { absolute: "Çerez politikası | Modaryo" },
    description: "Sitede kullanılan çerezler ve tercihleriniz.",
};

export default function LegalCerezPolitikasiPage() {
    return (
        <LegalPage title="Çerez politikası">
            <Content />
        </LegalPage>
    );
}
