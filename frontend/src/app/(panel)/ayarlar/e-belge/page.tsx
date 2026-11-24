import type { Metadata } from "next";
import EBelgeSettingsForm from "@/components/settings/EBelgeSettingsForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "e-Belge" };

export default function Page() {
    return (
        <SettingsPage category="ebelge" title="e-Belge" description="e-Fatura ve e-Arşiv kullanımı.">
            <EBelgeSettingsForm />
        </SettingsPage>
    );
}
