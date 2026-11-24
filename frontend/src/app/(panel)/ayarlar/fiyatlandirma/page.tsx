import type { Metadata } from "next";
import PricingSettingsForm from "@/components/settings/PricingSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Fiyatlandırma Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="fiyat" title="Fiyatlandırma" description="Varsayılan fiyat listeleri, KDV gösterimi ve yuvarlama kuralı.">
            <PricingSettingsForm />
        </SettingsPage>
    );
}
