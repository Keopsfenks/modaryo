import type { Metadata } from "next";
import CatalogSettingsForm from "@/components/settings/CatalogSettingsForm";
import CustomFieldDefinitions from "@/components/settings/CustomFieldDefinitions";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Katalog Ayarları" };

export default function Page() {
    return (
        <SettingsPage category="katalog" title="Katalog" description="Ürün kodu şablonu, KDV, birimler, barkod önekleri ve özel alanlar.">
            <CatalogSettingsForm />
            <CustomFieldDefinitions />
        </SettingsPage>
    );
}
