import type { Metadata } from "next";
import ContactPage from "@/components/site/ContactPage";

export const metadata: Metadata = {
    title: { absolute: "Demo iste | Modaryo" },
    description: "Modaryo'yu kendi işinizle birlikte görmek için demo talep edin. Formu doldurun, size dönelim.",
};

export default function DemoPage() {
    return (
        <ContactPage
            topic="demo"
            title="Modaryo'yu kendi işinizle görün."
            lead="Kısa bir formu doldurun. İşinizi dinleyip hangi modüllerin size uyduğunu birlikte bakalım."
            points={["Kart bilgisi istemiyoruz.", "Hazır olmayan modüller için haber listesine de yazılabilirsiniz.", "Bilgileriniz yalnızca bu talep için kullanılır."]}
        />
    );
}
