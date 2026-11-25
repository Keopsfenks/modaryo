import type { Metadata } from "next";
import PurchasingSettingsForm from "@/components/settings/PurchasingSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Satın Alma Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="satinalma" title="Satın alma" description="Teslimat toleransı, fiyat farkı eşiği, karantina ve masraf dağıtımı.">
            <PurchasingSettingsForm />
        </SettingsPage>
    );
}
