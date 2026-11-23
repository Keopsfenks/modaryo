import type { Metadata } from "next";
import GeneralSettingsForm from "@/components/settings/GeneralSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Genel Ayarlar" };

export default function Page() {
    return (
        <SettingsPage category="genel" title="Genel Ayarlar" description="Para birimi, ondalıklar, tarih biçimi ve çalışma takvimi.">
            <GeneralSettingsForm />
        </SettingsPage>
    );
}
