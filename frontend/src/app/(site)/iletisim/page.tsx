import type { Metadata } from "next";
import ContactPage from "@/components/site/ContactPage";

export const metadata: Metadata = {
    title: { absolute: "İletişim | Modaryo" },
    description: "Modaryo ekibine soru, öneri ya da iş birliği için yazın.",
};

export default function ContactRoute() {
    return (
        <ContactPage
            topic="iletisim"
            title="Bize yazın."
            lead="Sorunuzu, önerinizi ya da iş birliği fikrinizi iletin; en kısa sürede dönüş yapalım."
            points={["Kayıtlı bir hesabınız varsa panelden de yardım alabilirsiniz.", "Bilgileriniz yalnızca bu mesaj için kullanılır."]}
        />
    );
}
