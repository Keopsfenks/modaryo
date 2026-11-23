import type { Metadata } from "next";
import ModulesForm from "@/components/settings/ModulesForm";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Modüller" };

export default function Page() {
    return (
        <SettingsPage category="moduller" title="Modüller" description="Firmanızda açık olan modülleri yönetin.">
            <ModulesForm />
        </SettingsPage>
    );
}
