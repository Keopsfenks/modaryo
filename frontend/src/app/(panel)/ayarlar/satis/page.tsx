import type { Metadata } from "next";
import SalesSettingsForm from "@/components/settings/SalesSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Satış Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="satis" title="Satış" description="Teklif geçerliliği, satış temsilcisi zorunluluğu ve taksit varsayılanları.">
            <SalesSettingsForm />
        </SettingsPage>
    );
}
