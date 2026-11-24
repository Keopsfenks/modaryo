import type { Metadata } from "next";
import DocumentSettingsForm from "@/components/settings/DocumentSettingsForm";
import SeriesTable from "@/components/settings/SeriesTable";
import SettingsPage from "@/components/settings/SettingsPage";

export const metadata: Metadata = { title: "Belgeler" };

export default function Page() {
    return (
        <SettingsPage category="belgeler" title="Belgeler" description="Fatura PDF görünümü, e-posta şablonları ve belge serileri.">
            <DocumentSettingsForm />
            <div style={{ marginTop: 24 }}>
                <SeriesTable />
            </div>
        </SettingsPage>
    );
}
