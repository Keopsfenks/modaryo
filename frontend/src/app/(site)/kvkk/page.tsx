import type { Metadata } from "next";
import LegalPage from "@/components/site/LegalPage";
import Content from "@/content/site/legal/kvkk.mdx";

export const metadata: Metadata = {
    title: { absolute: "KVKK aydınlatma metni | Modaryo" },
    description: "Kişisel verilerinizin işlenmesine ilişkin aydınlatma metni.",
};

export default function LegalKvkkPage() {
    return (
        <LegalPage title="KVKK aydınlatma metni">
            <Content />
        </LegalPage>
    );
}
