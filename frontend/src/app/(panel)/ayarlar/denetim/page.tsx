import type { Metadata } from "next";
import AuditLog from "@/components/settings/AuditLog";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Denetim Kaydı" };

export default function Page() {
    return (
        <SettingsPage category="denetim" title="Denetim Kaydı" description="Firmada yapılan değişikliklerin geçmişi.">
            <AuditLog />
        </SettingsPage>
    );
}
