import type { Metadata } from "next";
import SettingsPage from "@/components/settings/SettingsPage";
import ShippingSettingsForm from "@/components/settings/ShippingSettingsForm";

export const metadata: Metadata = { title: "Sevkiyat Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="sevkiyat" title="Sevkiyat" description="Toplama-paketleme akışı, müşteri bildirimi ve kargo firmaları.">
            <ShippingSettingsForm />
        </SettingsPage>
    );
}
